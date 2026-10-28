using BackMaster.Core.Filtering;
using BackMaster.Core.Ipc;
using BackMaster.Core.Scheduling;
using BackMaster.Core.Security;
using Xunit;

namespace BackMaster.Core.Tests.Ipc;

public class RequestHandlerTests
{
    private static readonly string Verifier = PhraseVerifier.Create("отвори настройките", 100_000);
    private static readonly ServiceStatus SampleStatus =
        new(true, EnforcementMode.Scheduled, DateTimeOffset.Parse("2026-10-28T10:30:00Z"), "Europe/Sofia", 36, "Inside class period.");

    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-28T09:05:00Z");

    private RequestHandler Handler(string? verifier = null, AttemptThrottle? throttle = null) =>
        new(() => SampleStatus, () => verifier ?? Verifier, throttle ?? new AttemptThrottle(), () => _now);

    [Fact]
    public void Status_is_returned_over_the_wire_format()
    {
        var response = ServiceProtocol.Decode<ServiceResponse>(Handler().HandleLine("""{"command":"status"}"""));
        Assert.NotNull(response);
        Assert.True(response!.Ok);
        Assert.Equal(SampleStatus, response.Status);
    }

    [Fact]
    public void The_correct_phrase_unlocks()
    {
        var r = Handler().Handle(new ServiceRequest(Commands.Unlock, "отвори настройките"));
        Assert.True(r.Ok);
        Assert.Equal(UnlockResult.Accepted, r.Unlock);
    }

    [Fact]
    public void A_wrong_phrase_is_rejected_then_locked_out()
    {
        var h = Handler(throttle: new AttemptThrottle(2, TimeSpan.FromSeconds(30)));
        Assert.Equal(UnlockResult.Rejected, h.Handle(new ServiceRequest(Commands.Unlock, "грешна фраза")).Unlock);

        var locked = h.Handle(new ServiceRequest(Commands.Unlock, "грешна фраза"));
        Assert.Equal(UnlockResult.LockedOut, locked.Unlock);
        Assert.Equal(30, locked.RetryAfterSeconds);

        // Even the right phrase waits out the lockout, so guessing gains nothing.
        _now = _now.AddSeconds(5);
        var stillLocked = h.Handle(new ServiceRequest(Commands.Unlock, "отвори настройките"));
        Assert.False(stillLocked.Ok);
        Assert.Equal(25, stillLocked.RetryAfterSeconds);

        _now = _now.AddSeconds(25);
        Assert.True(h.Handle(new ServiceRequest(Commands.Unlock, "отвори настройките")).Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext-in-the-file")]
    public void Without_a_valid_verifier_nothing_unlocks(string stored)
    {
        var r = Handler(verifier: stored).Handle(new ServiceRequest(Commands.Unlock, stored));
        Assert.False(r.Ok);
        Assert.Equal(UnlockResult.NotConfigured, r.Unlock);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"command":"format-disk"}""")]
    public void Garbage_and_unknown_commands_are_refused(string line)
    {
        var r = ServiceProtocol.Decode<ServiceResponse>(Handler().HandleLine(line));
        Assert.False(r!.Ok);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Oversized_requests_are_refused_without_parsing()
    {
        var huge = "{\"command\":\"unlock\",\"phrase\":\"" + new string('a', ServiceProtocol.MaxRequestChars) + "\"}";
        Assert.False(ServiceProtocol.Decode<ServiceResponse>(Handler().HandleLine(huge))!.Ok);
    }

    [Fact]
    public void Status_carries_blocked_attempts_after_the_cursor()
    {
        var feed = new AttemptFeed();
        var h = new RequestHandler(() => SampleStatus, () => Verifier, new AttemptThrottle(), () => _now, feed);
        feed.Record(new BlockedSite("chatgpt.com", true), _now);

        // A new tray gets the cursor but not the old attempt.
        var first = h.Handle(new ServiceRequest(Commands.Status));
        Assert.Equal(1, first.Cursor);
        Assert.Empty(first.Attempts!);

        feed.Record(new BlockedSite("poki.com", false), _now);
        var next = ServiceProtocol.Decode<ServiceResponse>(h.HandleLine("""{"command":"status","after":1}"""))!;
        Assert.Equal(2, next.Cursor);
        var attempt = Assert.Single(next.Attempts!);
        Assert.Equal("poki.com", attempt.Domain);
        Assert.False(attempt.IsAi);

        Assert.Empty(h.Handle(new ServiceRequest(Commands.Status, After: 2)).Attempts!);
    }
}
