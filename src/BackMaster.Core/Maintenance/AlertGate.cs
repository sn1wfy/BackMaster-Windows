namespace BackMaster.Core.Maintenance;

public enum AlertKind
{
    None,
    /// <summary>Health got worse, or a different set of problems appeared.</summary>
    Problem,
    /// <summary>The same problem is still there after the reminder interval.</summary>
    Reminder,
    /// <summary>The installation is healthy again after a problem.</summary>
    Recovered,
}

/// <summary>What the gate remembers between checks; persisted in health.json.</summary>
public sealed record AlertState(HealthStatus LastStatus, string LastFingerprint, DateTimeOffset? LastAlertAt)
{
    public static AlertState Initial => new(HealthStatus.Healthy, "", null);
}

public readonly record struct AlertDecision(AlertKind Kind, AlertState NextState);

/// <summary>
/// Decides when the administrator should be told about a health check. The
/// maintenance task runs every few minutes, so repeating the same alert each time
/// would train the teacher to ignore it.
/// </summary>
public sealed class AlertGate
{
    public TimeSpan ReminderInterval { get; }

    public AlertGate(TimeSpan? reminderInterval = null)
    {
        ReminderInterval = reminderInterval ?? TimeSpan.FromHours(12);
        if (ReminderInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reminderInterval), "The reminder interval must be positive.");
    }

    public static string Fingerprint(HealthReport report) =>
        string.Join("|", report.Issues
            .Select(i => $"{i.Kind}:{i.Subject}")
            .OrderBy(s => s, StringComparer.Ordinal));

    public AlertDecision Decide(AlertState previous, HealthReport report, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(report);
        string fp = Fingerprint(report);

        if (report.Status == HealthStatus.Healthy)
        {
            var kind = previous.LastStatus == HealthStatus.Healthy ? AlertKind.None : AlertKind.Recovered;
            return new(kind, new AlertState(HealthStatus.Healthy, "", kind == AlertKind.None ? previous.LastAlertAt : now));
        }

        bool worse = report.Status > previous.LastStatus;
        bool different = fp != previous.LastFingerprint;
        if (worse || different)
            return new(AlertKind.Problem, new AlertState(report.Status, fp, now));

        bool due = previous.LastAlertAt is null || now - previous.LastAlertAt.Value >= ReminderInterval;
        return due
            ? new(AlertKind.Reminder, previous with { LastAlertAt = now })
            : new(AlertKind.None, previous);
    }
}
