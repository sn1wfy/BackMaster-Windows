using BackMaster.Core.Security;
using Xunit;

namespace BackMaster.Core.Tests.Security;

public class PhraseVerifierTests
{
    // The minimum allowed count keeps the tests fast; production uses DefaultIterations.
    private const int Fast = 100_000;

    [Fact]
    public void The_right_phrase_verifies_and_a_wrong_one_does_not()
    {
        var v = PhraseVerifier.Create("лисицата учи C#", Fast);
        Assert.True(PhraseVerifier.Verify("лисицата учи C#", v));
        Assert.False(PhraseVerifier.Verify("лисицата учи c#", v));
        Assert.False(PhraseVerifier.Verify("", v));
        Assert.False(PhraseVerifier.Verify(null, v));
    }

    [Fact]
    public void The_phrase_is_not_stored()
    {
        var v = PhraseVerifier.Create("correct horse battery", Fast);
        Assert.DoesNotContain("correct", v);
        Assert.StartsWith("pbkdf2-sha256$100000$", v);
        Assert.Equal(4, v.Split('$').Length);
    }

    [Fact]
    public void Each_verifier_has_its_own_salt()
    {
        Assert.NotEqual(PhraseVerifier.Create("correct horse battery", Fast), PhraseVerifier.Create("correct horse battery", Fast));
    }

    [Fact]
    public void Default_iterations_follow_current_guidance()
    {
        Assert.True(PhraseVerifier.DefaultIterations >= 600_000);
    }

    [Fact]
    public void Surrounding_spaces_and_unicode_forms_do_not_matter()
    {
        // "й" typed as one code point vs. "и" + combining breve.
        var v = PhraseVerifier.Create("тайна фраза", Fast);
        Assert.True(PhraseVerifier.Verify("  тай̆на фраза \t".Replace("тай̆", "тай"), v));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaa")]
    [InlineData("         ")]
    public void Weak_phrases_are_refused(string phrase)
    {
        Assert.NotNull(PhraseVerifier.Weakness(phrase));
        Assert.Throws<ArgumentException>(() => PhraseVerifier.Create(phrase, Fast));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plaintext phrase")]
    [InlineData("sha1$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$1$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$2000000000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("pbkdf2-sha256$600000$!!!$AAAA")]
    [InlineData("pbkdf2-sha256$600000$AAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Malformed_or_dangerous_verifiers_never_match(string? verifier)
    {
        Assert.False(PhraseVerifier.IsWellFormed(verifier));
        Assert.False(PhraseVerifier.Verify("anything at all", verifier));
    }
}

public class AttemptThrottleTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-28T11:00:00Z");

    [Fact]
    public void The_first_mistakes_are_free_then_lockouts_double_up_to_the_cap()
    {
        var t = new AttemptThrottle(3, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(35));
        Assert.Equal(TimeSpan.Zero, t.RecordFailure(T0));
        Assert.Equal(TimeSpan.Zero, t.RecordFailure(T0));
        Assert.Equal(TimeSpan.FromSeconds(10), t.RecordFailure(T0));
        Assert.Equal(TimeSpan.FromSeconds(20), t.RecordFailure(T0));
        Assert.Equal(TimeSpan.FromSeconds(35), t.RecordFailure(T0));
        Assert.Equal(TimeSpan.FromSeconds(35), t.RecordFailure(T0));
    }

    [Fact]
    public void Attempts_are_refused_until_the_lockout_ends()
    {
        var t = new AttemptThrottle(1, TimeSpan.FromSeconds(30));
        Assert.True(t.TryBegin(T0, out _));
        t.RecordFailure(T0);
        Assert.False(t.TryBegin(T0.AddSeconds(10), out var wait));
        Assert.Equal(TimeSpan.FromSeconds(20), wait);
        Assert.True(t.TryBegin(T0.AddSeconds(30), out _));
    }

    [Fact]
    public void Success_resets_the_count()
    {
        var t = new AttemptThrottle(2, TimeSpan.FromSeconds(30));
        t.RecordFailure(T0);
        t.RecordSuccess();
        Assert.Equal(TimeSpan.Zero, t.RecordFailure(T0));
        Assert.True(t.TryBegin(T0, out _));
    }
}
