using BackMaster.Core.Filtering;
using BackMaster.Core.Security;

namespace BackMaster.Core.Ipc;

/// <summary>
/// The service's side of the protocol, independent of the pipe so it can be tested.
/// </summary>
public sealed class RequestHandler
{
    private readonly Func<ServiceStatus> _status;
    private readonly Func<string?> _loadVerifier;
    private readonly AttemptThrottle _throttle;
    private readonly Func<DateTimeOffset> _clock;
    private readonly AttemptFeed? _attempts;

    /// <param name="loadVerifier">Reads the stored verifier, or returns null if none is set.</param>
    public RequestHandler(Func<ServiceStatus> status, Func<string?> loadVerifier, AttemptThrottle throttle,
                          Func<DateTimeOffset>? clock = null, AttemptFeed? attempts = null)
    {
        _attempts = attempts;
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _loadVerifier = loadVerifier ?? throw new ArgumentNullException(nameof(loadVerifier));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string HandleLine(string line)
    {
        var request = line.Length <= ServiceProtocol.MaxRequestChars ? ServiceProtocol.Decode<ServiceRequest>(line) : null;
        return ServiceProtocol.Encode(request is null ? new ServiceResponse(false, "Malformed request.") : Handle(request));
    }

    public ServiceResponse Handle(ServiceRequest request) => request.Command switch
    {
        Commands.Status => Status(request.After),
        Commands.Unlock => Unlock(request.Phrase),
        _ => new ServiceResponse(false, $"Unknown command '{request.Command}'."),
    };

    /// <summary>
    /// The status, plus the blocked attempts after the caller's cursor. A caller without a
    /// cursor gets only the current cursor, so it does not replay attempts from before it started.
    /// </summary>
    private ServiceResponse Status(long? after)
    {
        if (_attempts is null) return new ServiceResponse(true, Status: _status());
        long cursor = _attempts.Cursor;
        var attempts = after is long a ? _attempts.After(a) : Array.Empty<BlockedAttempt>();
        return new ServiceResponse(true, Status: _status(), Attempts: attempts, Cursor: attempts.Count > 0 ? attempts[^1].Id : cursor);
    }

    private ServiceResponse Unlock(string? phrase)
    {
        var now = _clock();
        if (!_throttle.TryBegin(now, out var wait))
            return new ServiceResponse(false, Unlock: UnlockResult.LockedOut, RetryAfterSeconds: Seconds(wait));

        string? verifier = _loadVerifier();
        if (!PhraseVerifier.IsWellFormed(verifier))
            return new ServiceResponse(false, Unlock: UnlockResult.NotConfigured);

        if (PhraseVerifier.Verify(phrase, verifier))
        {
            _throttle.RecordSuccess();
            return new ServiceResponse(true, Unlock: UnlockResult.Accepted);
        }

        var lockout = _throttle.RecordFailure(now);
        return lockout > TimeSpan.Zero
            ? new ServiceResponse(false, Unlock: UnlockResult.LockedOut, RetryAfterSeconds: Seconds(lockout))
            : new ServiceResponse(false, Unlock: UnlockResult.Rejected);
    }

    private static int Seconds(TimeSpan t) => (int)Math.Ceiling(t.TotalSeconds);
}
