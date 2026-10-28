using BackMaster.Core.Scheduling;
using Xunit;

namespace BackMaster.Core.Tests.Scheduling;

// Calendar facts used below (Europe/Sofia):
//   2026-10-28 is a Wednesday, after DST ended on 2026-10-25  -> UTC+02:00
//   2026-07-01 is a Wednesday, in summer time                   -> UTC+03:00
//   DST starts again on 2027-03-28
public class ClassScheduleTests
{
    private static readonly TimeZoneInfo Sofia = TimeZoneResolver.Resolve("Europe/Sofia");

    private static ClassSchedule Default(EnforcementMode mode = EnforcementMode.Scheduled) =>
        new(ClassSchedule.DefaultWindows, Sofia, mode);

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso);

    [Theory]
    // Wednesday 11:00-12:30
    [InlineData("2026-10-28T10:59:59+02:00", false)]
    [InlineData("2026-10-28T11:00:00+02:00", true)]
    [InlineData("2026-10-28T11:45:00+02:00", true)]
    [InlineData("2026-10-28T12:29:59+02:00", true)]
    [InlineData("2026-10-28T12:30:00+02:00", false)]
    // Thursday 09:10-10:50
    [InlineData("2026-10-29T09:09:59+02:00", false)]
    [InlineData("2026-10-29T09:10:00+02:00", true)]
    [InlineData("2026-10-29T10:49:59+02:00", true)]
    [InlineData("2026-10-29T10:50:00+02:00", false)]
    // The other day's hours do not count
    [InlineData("2026-10-28T09:30:00+02:00", false)]
    [InlineData("2026-10-29T11:30:00+02:00", false)]
    // Every other day is free
    [InlineData("2026-10-26T11:30:00+02:00", false)] // Monday
    [InlineData("2026-10-27T11:30:00+02:00", false)] // Tuesday
    [InlineData("2026-10-30T09:30:00+02:00", false)] // Friday
    [InlineData("2026-10-31T11:30:00+02:00", false)] // Saturday
    [InlineData("2026-11-01T09:30:00+02:00", false)] // Sunday
    public void Enforces_only_inside_class_periods(string instant, bool expected)
    {
        Assert.Equal(expected, Default().Evaluate(At(instant)).Enforce);
    }

    [Theory]
    // Summer: 11:00 Sofia is 08:00 UTC
    [InlineData("2026-07-01T07:59:59Z", false)]
    [InlineData("2026-07-01T08:00:00Z", true)]
    [InlineData("2026-07-01T09:29:59Z", true)]
    [InlineData("2026-07-01T09:30:00Z", false)]
    // Winter: 11:00 Sofia is 09:00 UTC
    [InlineData("2026-10-28T08:59:59Z", false)]
    [InlineData("2026-10-28T09:00:00Z", true)]
    public void Follows_daylight_saving_in_the_schedule_zone(string instant, bool expected)
    {
        Assert.Equal(expected, Default().Evaluate(At(instant)).Enforce);
    }

    [Fact]
    public void Changing_the_PC_time_zone_does_not_move_the_lesson()
    {
        // A student switches the PC to Hawaii (-10:00). 2026-10-27 23:00 there is
        // 2026-10-28 11:00 in Sofia: the lesson is on.
        Assert.True(Default().Evaluate(At("2026-10-27T23:00:00-10:00")).Enforce);
        // A Hawaii wall clock reading "Wednesday 11:00" is 23:00 in Sofia: free time.
        Assert.False(Default().Evaluate(At("2026-10-28T11:00:00-10:00")).Enforce);
        // Kiribati (+14:00): Thursday 01:15 there is Wednesday 13:15 in Sofia: free time.
        Assert.False(Default().Evaluate(At("2026-10-29T01:15:00+14:00")).Enforce);
        // Kiribati: Wednesday 23:00 there is 09:00 UTC = 11:00 Sofia: the lesson is on.
        Assert.True(Default().Evaluate(At("2026-10-28T23:00:00+14:00")).Enforce);
    }

    [Theory]
    // Inside Wednesday's lesson: next change is its end, 12:30 Sofia.
    [InlineData("2026-10-28T11:15:00+02:00", "2026-10-28T10:30:00Z")]
    // Before Wednesday's lesson: today 11:00.
    [InlineData("2026-10-28T10:00:00+02:00", "2026-10-28T09:00:00Z")]
    // Right as Wednesday's lesson ends: Thursday 09:10.
    [InlineData("2026-10-28T12:30:00+02:00", "2026-10-29T07:10:00Z")]
    // Right as Thursday's lesson ends: next Wednesday 11:00.
    [InlineData("2026-10-29T10:50:00+02:00", "2026-11-04T09:00:00Z")]
    // Weekend: next Wednesday.
    [InlineData("2026-11-01T18:00:00+02:00", "2026-11-04T09:00:00Z")]
    // Thursday before DST starts: next Wednesday is already summer time (+03:00).
    [InlineData("2027-03-25T12:00:00+02:00", "2027-03-31T08:00:00Z")]
    public void Reports_when_the_state_next_changes(string instant, string expectedUtc)
    {
        Assert.Equal(At(expectedUtc), Default().Evaluate(At(instant)).NextChangeUtc);
    }

    [Fact]
    public void Reports_the_active_period()
    {
        var d = Default().Evaluate(At("2026-10-29T10:00:00+02:00"));
        Assert.Equal(new ClassWindow(DayOfWeek.Thursday, new TimeOnly(9, 10), new TimeOnly(10, 50)), d.ActiveWindow);
    }

    [Theory]
    [InlineData("2026-10-28T11:15:00+02:00")] // inside a lesson
    [InlineData("2026-10-31T15:00:00+02:00")] // Saturday
    public void Administrator_modes_override_the_timetable(string instant)
    {
        var on = Default(EnforcementMode.AlwaysOn).Evaluate(At(instant));
        var off = Default(EnforcementMode.AlwaysOff).Evaluate(At(instant));
        Assert.True(on.Enforce);
        Assert.False(off.Enforce);
        Assert.Null(on.NextChangeUtc);
        Assert.Null(off.NextChangeUtc);
    }

    [Fact]
    public void No_periods_means_never_enforced()
    {
        var d = new ClassSchedule(Array.Empty<ClassWindow>(), Sofia).Evaluate(At("2026-10-28T11:15:00+02:00"));
        Assert.False(d.Enforce);
        Assert.Null(d.NextChangeUtc);
    }

    [Theory]
    [InlineData(12, 30, 11, 0)]
    [InlineData(11, 0, 11, 0)]
    [InlineData(23, 0, 1, 0)] // crossing midnight is rejected, not silently misread
    public void Invalid_periods_are_rejected(int sh, int sm, int eh, int em)
    {
        Assert.Throws<ArgumentException>(() =>
            new ClassWindow(DayOfWeek.Wednesday, new TimeOnly(sh, sm), new TimeOnly(eh, em)));
    }

    [Fact]
    public void Overlapping_and_touching_periods_merge()
    {
        var s = new ClassSchedule(new[]
        {
            new ClassWindow(DayOfWeek.Wednesday, new TimeOnly(11, 45), new TimeOnly(12, 30)),
            new ClassWindow(DayOfWeek.Wednesday, new TimeOnly(11, 0), new TimeOnly(11, 45)),
            new ClassWindow(DayOfWeek.Wednesday, new TimeOnly(12, 0), new TimeOnly(12, 15)),
        }, Sofia);

        Assert.Single(s.Windows);
        var d = s.Evaluate(At("2026-10-28T11:45:00+02:00"));
        Assert.True(d.Enforce);
        Assert.Equal(At("2026-10-28T10:30:00Z"), d.NextChangeUtc);
    }
}

public class TimeZoneResolverTests
{
    [Theory]
    [InlineData("Europe/Sofia")]
    [InlineData("FLE Standard Time")]
    [InlineData("  Europe/Sofia  ")]
    public void Accepts_iana_and_windows_ids(string id)
    {
        // Off Windows, "FLE Standard Time" maps to the legacy name Europe/Kiev, which
        // minimal Linux tzdata images omit. Classroom PCs run Windows, where the
        // Windows id resolves natively, so that case is only checked there.
        if (!OperatingSystem.IsWindows() && id.Contains("Standard Time")) return;

        Assert.True(TimeZoneResolver.TryResolve(id, out var zone));
        Assert.Equal(TimeSpan.FromHours(3), zone.GetUtcOffset(DateTimeOffset.Parse("2026-07-01T12:00:00Z")));
        Assert.Equal(TimeSpan.FromHours(2), zone.GetUtcOffset(DateTimeOffset.Parse("2026-12-01T12:00:00Z")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void Rejects_unknown_ids(string? id)
    {
        Assert.False(TimeZoneResolver.TryResolve(id, out _));
        Assert.Throws<TimeZoneNotFoundException>(() => TimeZoneResolver.Resolve(id));
    }
}
