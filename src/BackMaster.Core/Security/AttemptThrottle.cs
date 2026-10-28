namespace BackMaster.Core.Security;

/// <summary>
/// Slows down phrase guessing. The first few mistakes are free (teachers mistype); after
/// that each failure doubles a lockout, up to a cap. A correct phrase resets everything.
/// One instance lives in the service, so restarting the tray does not reset it.
/// </summary>
public sealed class AttemptThrottle
{
    private readonly object _gate = new();
    private int _failures;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    public int FreeAttempts { get; }
    public TimeSpan BaseLockout { get; }
    public TimeSpan MaxLockout { get; }

    public AttemptThrottle(int freeAttempts = 5, TimeSpan? baseLockout = null, TimeSpan? maxLockout = null)
    {
        if (freeAttempts < 1) throw new ArgumentOutOfRangeException(nameof(freeAttempts));
        FreeAttempts = freeAttempts;
        BaseLockout = baseLockout ?? TimeSpan.FromSeconds(30);
        MaxLockout = maxLockout ?? TimeSpan.FromMinutes(15);
    }

    /// <summary>Whether a guess may be checked now; if not, how long to wait.</summary>
    public bool TryBegin(DateTimeOffset now, out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            retryAfter = _lockedUntil > now ? _lockedUntil - now : TimeSpan.Zero;
            return retryAfter == TimeSpan.Zero;
        }
    }

    /// <returns>The lockout now in force (zero while attempts are still free).</returns>
    public TimeSpan RecordFailure(DateTimeOffset now)
    {
        lock (_gate)
        {
            _failures++;
            int over = _failures - FreeAttempts;
            if (over < 0) return TimeSpan.Zero;
            double seconds = Math.Min(BaseLockout.TotalSeconds * Math.Pow(2, Math.Min(over, 30)), MaxLockout.TotalSeconds);
            var lockout = TimeSpan.FromSeconds(seconds);
            _lockedUntil = now + lockout;
            return lockout;
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failures = 0;
            _lockedUntil = DateTimeOffset.MinValue;
        }
    }
}
