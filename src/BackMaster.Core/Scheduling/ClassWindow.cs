namespace BackMaster.Core.Scheduling;

/// <summary>
/// One recurring class period, e.g. Wednesday 11:00-12:30. <see cref="Start"/> is
/// inclusive and <see cref="End"/> exclusive, so a period ending at 12:30 is over at
/// exactly 12:30:00. A period may not cross midnight.
/// </summary>
public sealed record ClassWindow
{
    public DayOfWeek Day { get; }
    public TimeOnly Start { get; }
    public TimeOnly End { get; }

    public ClassWindow(DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        if (!Enum.IsDefined(day))
            throw new ArgumentOutOfRangeException(nameof(day), day, "Unknown day of week.");
        if (end <= start)
            throw new ArgumentException(
                $"A class period must end after it starts ({start:HH\\:mm}-{end:HH\\:mm}). " +
                "Periods that cross midnight are not supported.", nameof(end));
        Day = day;
        Start = start;
        End = end;
    }

    public bool Contains(DayOfWeek day, TimeOnly time) => day == Day && time >= Start && time < End;

    public override string ToString() => $"{Day} {Start:HH\\:mm}-{End:HH\\:mm}";
}
