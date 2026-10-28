using System.Globalization;
using System.Text.Json;
using BackMaster.Core.Filtering;
using BackMaster.Core.Ipc;
using BackMaster.Core.Scheduling;

namespace BackMaster.Core.Companion;

public sealed record TitledMessage(string Title, string Body);

/// <param name="Chip">Short category label shown above the title, e.g. "AI АСИСТЕНТ".</param>
/// <param name="Hint">What the student can do instead; may be empty.</param>
public sealed record FoxMessage(string Chip, string Title, string Body, string Hint);

/// <summary>
/// The fox's student-facing texts from assets/companion/messages.bg.json, with the
/// {end}/{next}/{domain}/{time} placeholders filled in. Times are shown in the
/// schedule's zone, so they match the timetable on the wall.
/// </summary>
public sealed class CompanionMessages
{
    private static readonly CultureInfo Bulgarian = CultureInfo.GetCultureInfo("bg-BG");

    public required string TrayActive { get; init; }
    public required string TrayOffHours { get; init; }
    public required string TrayAlwaysOn { get; init; }
    public required string TrayPaused { get; init; }
    public required string TrayUnreachable { get; init; }
    public required TitledMessage LessonStarting { get; init; }
    public required TitledMessage LessonEnding { get; init; }
    public required IReadOnlyList<string> Tips { get; init; }
    public required string TipPrefix { get; init; }
    public required FoxMessage AiBlocked { get; init; }
    public required FoxMessage SiteBlocked { get; init; }
    public required string Dismiss { get; init; }

    public static CompanionMessages Load(string path) => Parse(File.ReadAllText(path));

    public static CompanionMessages Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var tray = root.GetProperty("tray");
        var lesson = root.GetProperty("lesson");
        var messages = root.GetProperty("messages");
        return new CompanionMessages
        {
            TrayActive = tray.GetProperty("active").GetString()!,
            TrayOffHours = tray.GetProperty("off_hours").GetString()!,
            TrayAlwaysOn = tray.GetProperty("always_on").GetString()!,
            TrayPaused = tray.GetProperty("paused").GetString()!,
            TrayUnreachable = tray.GetProperty("service_unreachable").GetString()!,
            LessonStarting = Titled(lesson.GetProperty("starting")),
            LessonEnding = Titled(lesson.GetProperty("ending")),
            Tips = root.TryGetProperty("tips", out var tips) ? tips.EnumerateArray().Select(t => t.GetString()!).ToList() : Array.Empty<string>(),
            TipPrefix = root.TryGetProperty("tip_prefix", out var prefix) ? prefix.GetString()! : "",
            AiBlocked = Fox(messages.GetProperty("ai_blocked")),
            SiteBlocked = Fox(messages.GetProperty("site_blocked")),
            Dismiss = root.TryGetProperty("buttons", out var buttons) && buttons.TryGetProperty("dismiss", out var dismiss) ? dismiss.GetString()! : "OK",
        };
    }

    /// <summary>The tray tooltip for a status, or the "unreachable" text when there is none.</summary>
    public string TrayText(ServiceStatus? status)
    {
        if (status is null) return TrayUnreachable;
        return status.Mode switch
        {
            EnforcementMode.AlwaysOn => TrayAlwaysOn,
            EnforcementMode.AlwaysOff => TrayPaused,
            _ when status.Enforce => TrayActive.Replace("{end}", ClockTime(status.NextChangeUtc, status.TimeZone)),
            _ => TrayOffHours.Replace("{next}", DayAndTime(status.NextChangeUtc, status.TimeZone)),
        };
    }

    public TitledMessage Starting(ServiceStatus status) =>
        LessonStarting with { Body = LessonStarting.Body.Replace("{end}", ClockTime(status.NextChangeUtc, status.TimeZone)) };

    /// <summary>The fox's explanation for a blocked site, with {domain} filled in.</summary>
    public FoxMessage Blocked(BlockedAttempt attempt)
    {
        var m = attempt.IsAi ? AiBlocked : SiteBlocked;
        return m with { Body = m.Body.Replace("{domain}", attempt.Domain), Title = m.Title.Replace("{domain}", attempt.Domain) };
    }

    public string RandomTip(Random random) => Tips.Count == 0 ? "" : TipPrefix + Tips[random.Next(Tips.Count)];

    /// <summary>"12:30" in the schedule's zone.</summary>
    public static string ClockTime(DateTimeOffset? utc, string timeZone) =>
        utc is null ? "—" : Local(utc.Value, timeZone).ToString("HH:mm", Bulgarian);

    /// <summary>"сряда 11:00" in the schedule's zone.</summary>
    public static string DayAndTime(DateTimeOffset? utc, string timeZone) =>
        utc is null ? "няма" : Local(utc.Value, timeZone).ToString("dddd HH:mm", Bulgarian);

    private static DateTimeOffset Local(DateTimeOffset utc, string timeZone) =>
        TimeZoneInfo.ConvertTime(utc, TimeZoneResolver.TryResolve(timeZone, out var z) ? z : TimeZoneInfo.Local);

    private static FoxMessage Fox(JsonElement e) => new(
        e.GetProperty("chip").GetString()!, e.GetProperty("title").GetString()!,
        e.GetProperty("body").GetString()!, e.TryGetProperty("hint", out var h) ? h.GetString()! : "");

    private static TitledMessage Titled(JsonElement e) =>
        new(e.GetProperty("title").GetString()!, e.GetProperty("body").GetString()!);
}
