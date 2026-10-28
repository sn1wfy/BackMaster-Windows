namespace BackMaster.Core.Scheduling;

/// <summary>How the administrator wants enforcement driven.</summary>
public enum EnforcementMode
{
    /// <summary>Enforce only inside the configured class periods (the default).</summary>
    Scheduled,
    /// <summary>Enforce at all times, e.g. during an exam week.</summary>
    AlwaysOn,
    /// <summary>Never enforce; the machine behaves as if BackMaster were not installed.</summary>
    AlwaysOff,
}

/// <param name="Enforce">Whether restrictions apply right now.</param>
/// <param name="ActiveWindow">The class period in progress, if any.</param>
/// <param name="NextChangeUtc">When <paramref name="Enforce"/> will next flip, or null if never.</param>
/// <param name="Reason">A short English explanation for the diagnostics log.</param>
public readonly record struct ScheduleDecision(
    bool Enforce,
    ClassWindow? ActiveWindow,
    DateTimeOffset? NextChangeUtc,
    string Reason);

/// <summary>
/// Decides whether classroom restrictions are in force at a given instant.
///
/// Times are evaluated in a fixed, administrator-configured time zone rather than
/// the machine's current zone. Standard users cannot change the system clock on
/// Windows, but they can change the time zone; pinning the zone means doing so
/// cannot move the lesson. Input instants are absolute (DateTimeOffset), so daylight
/// saving is handled by the time zone rules.
/// </summary>
public sealed class ClassSchedule
{
    private readonly IReadOnlyList<ClassWindow> _windows;

    public TimeZoneInfo TimeZone { get; }
    public EnforcementMode Mode { get; }
    public IReadOnlyList<ClassWindow> Windows => _windows;

    public ClassSchedule(IEnumerable<ClassWindow> windows, TimeZoneInfo timeZone,
                         EnforcementMode mode = EnforcementMode.Scheduled)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown enforcement mode.");
        _windows = Merge(windows);
        TimeZone = timeZone;
        Mode = mode;
    }

    /// <summary>The two weekly lessons: Wednesday 11:00-12:30 and Thursday 09:10-10:50.</summary>
    public static IReadOnlyList<ClassWindow> DefaultWindows { get; } = new[]
    {
        new ClassWindow(DayOfWeek.Wednesday, new TimeOnly(11, 0), new TimeOnly(12, 30)),
        new ClassWindow(DayOfWeek.Thursday, new TimeOnly(9, 10), new TimeOnly(10, 50)),
    };

    public const string DefaultTimeZoneId = "Europe/Sofia";

    public ScheduleDecision Evaluate(DateTimeOffset now)
    {
        switch (Mode)
        {
            case EnforcementMode.AlwaysOn:
                return new ScheduleDecision(true, null, null, "Enforcement forced on by the administrator.");
            case EnforcementMode.AlwaysOff:
                return new ScheduleDecision(false, null, null, "Enforcement turned off by the administrator.");
        }

        if (_windows.Count == 0)
            return new ScheduleDecision(false, null, null, "No class periods are configured.");

        DateTime local = TimeZoneInfo.ConvertTime(now, TimeZone).DateTime;
        var time = TimeOnly.FromDateTime(local);
        foreach (var w in _windows)
        {
            if (w.Contains(local.DayOfWeek, time))
            {
                var end = ToUtc(DateOnly.FromDateTime(local), w.End);
                return new ScheduleDecision(true, w, end, $"Inside class period {w}.");
            }
        }

        var next = NextStart(local);
        return new ScheduleDecision(false, null, next, "Outside class periods.");
    }

    /// <summary>The next period start strictly after <paramref name="local"/>, as UTC.</summary>
    private DateTimeOffset? NextStart(DateTime local)
    {
        var today = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);
        // Eight days covers "same weekday next week" when today's period has passed.
        for (int offset = 0; offset <= 7; offset++)
        {
            var date = today.AddDays(offset);
            foreach (var w in _windows) // sorted by day then start
            {
                if (w.Day != date.DayOfWeek) continue;
                if (offset == 0 && w.Start <= time) continue;
                return ToUtc(date, w.Start);
            }
        }
        return null;
    }

    /// <summary>
    /// Converts a wall-clock time in the schedule's zone to an absolute instant. A time
    /// skipped by a DST jump is moved forward past the gap; an ambiguous time takes
    /// the earlier (daylight) offset. School hours never hit either case in Bulgaria,
    /// but the behaviour is defined for every zone.
    /// </summary>
    private DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var wall = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (TimeZone.IsInvalidTime(wall)) wall = wall.AddMinutes(1);
        TimeSpan offset = TimeZone.IsAmbiguousTime(wall)
            ? TimeZone.GetAmbiguousTimeOffsets(wall).Max()
            : TimeZone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset).ToUniversalTime();
    }

    /// <summary>Sorts periods and joins ones that overlap or touch on the same day.</summary>
    private static IReadOnlyList<ClassWindow> Merge(IEnumerable<ClassWindow> windows)
    {
        var sorted = windows
            .OrderBy(w => w.Day)
            .ThenBy(w => w.Start)
            .ToList();
        var merged = new List<ClassWindow>(sorted.Count);
        foreach (var w in sorted)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (last.Day == w.Day && w.Start <= last.End)
                {
                    var end = w.End > last.End ? w.End : last.End;
                    merged[^1] = new ClassWindow(last.Day, last.Start, end);
                    continue;
                }
            }
            merged.Add(w);
        }
        return merged;
    }
}
