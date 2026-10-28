using BackMaster.Core.Scheduling;

namespace BackMaster.Core.Policy;

/// <summary>
/// The administrator's settings, stored as %ProgramData%\BackMaster\policy.json.
/// Readable by every user (the tray needs the hotkey), writable by Administrators only.
/// The unlock phrase verifier is deliberately not part of it; see <see cref="Security.PhraseVerifier"/>.
/// </summary>
public sealed record ClassroomPolicy(
    int SchemaVersion,
    ScheduleSection Schedule,
    FilteringSection Filtering,
    AccessSection Access)
{
    public const int CurrentSchemaVersion = 1;

    public static ClassroomPolicy Default { get; } = new(
        CurrentSchemaVersion,
        new ScheduleSection(
            EnforcementMode.Scheduled,
            ClassSchedule.DefaultTimeZoneId,
            ClassSchedule.DefaultWindows.Select(PeriodSpec.From).ToList()),
        FilteringSection.Default,
        AccessSection.Default);
}

public sealed record ScheduleSection(EnforcementMode Mode, string TimeZone, IReadOnlyList<PeriodSpec> Periods);

/// <param name="Start">Wall-clock "HH:mm" in the schedule's time zone.</param>
public sealed record PeriodSpec(DayOfWeek Day, string Start, string End)
{
    public static PeriodSpec From(ClassWindow w) => new(w.Day, w.Start.ToString("HH\\:mm"), w.End.ToString("HH\\:mm"));
}

/// <param name="AiDomains">AI assistants; the fox explains these with the ai_blocked message.</param>
/// <param name="BlockedDomains">Any other site closed during lessons (games, video, ...).</param>
public sealed record FilteringSection(IReadOnlyList<string> AiDomains, IReadOnlyList<string> BlockedDomains)
{
    public static FilteringSection Default { get; } = new(
        new[]
        {
            "chatgpt.com", "chat.openai.com", "openai.com", "oaistatic.com", "oaiusercontent.com",
            "gemini.google.com", "bard.google.com", "aistudio.google.com",
            "claude.ai", "anthropic.com",
            "copilot.microsoft.com", "copilot.cloud.microsoft",
            "perplexity.ai", "poe.com", "deepseek.com", "chat.deepseek.com",
            "character.ai", "you.com", "phind.com", "chat.mistral.ai", "grok.com", "x.ai",
            "blackbox.ai", "codeium.com", "tabnine.com", "quillbot.com",
        },
        new[]
        {
            "poki.com", "crazygames.com", "y8.com", "friv.com", "miniclip.com", "coolmathgames.com",
            "roblox.com", "slither.io", "agar.io", "krunker.io",
        });
}

/// <param name="Hotkey">Global shortcut that opens the phrase dialog, e.g. "Ctrl+Alt+Shift+B".</param>
public sealed record AccessSection(string Hotkey)
{
    public static AccessSection Default { get; } = new("Ctrl+Alt+Shift+B");
}
