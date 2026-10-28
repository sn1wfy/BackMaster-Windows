using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackMaster.Core.Filtering;
using BackMaster.Core.Hotkeys;
using BackMaster.Core.Scheduling;

namespace BackMaster.Core.Policy;

/// <summary>Reads, validates and writes policy.json. Every rejection says what is wrong.</summary>
public static class PolicyLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ClassroomPolicy Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// Parses and validates a policy. Missing "filtering" or "access" sections take their
    /// defaults, so the original schedule-only policy.json keeps working.
    /// </summary>
    public static ClassroomPolicy Parse(string json)
    {
        ClassroomPolicy? p;
        try
        {
            p = JsonSerializer.Deserialize<ClassroomPolicy>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"policy.json is not valid JSON: {e.Message}", e);
        }
        if (p is null) throw new InvalidDataException("policy.json is empty.");

        p = p with
        {
            Filtering = p.Filtering is null
                ? FilteringSection.Default
                : new FilteringSection(p.Filtering.AiDomains ?? Array.Empty<string>(), p.Filtering.BlockedDomains ?? Array.Empty<string>()),
            Access = p.Access ?? AccessSection.Default,
        };
        Validate(p);
        return p;
    }

    public static string Serialize(ClassroomPolicy policy) => JsonSerializer.Serialize(policy, JsonOptions);

    public static void Validate(ClassroomPolicy p)
    {
        var errors = Errors(p);
        if (errors.Count > 0)
            throw new InvalidDataException("policy.json is invalid:\n  - " + string.Join("\n  - ", errors));
    }

    public static IReadOnlyList<string> Errors(ClassroomPolicy p)
    {
        var errors = new List<string>();
        if (p.SchemaVersion != ClassroomPolicy.CurrentSchemaVersion)
            errors.Add($"schemaVersion {p.SchemaVersion} is not supported (expected {ClassroomPolicy.CurrentSchemaVersion}).");

        var s = p.Schedule;
        if (s is null) errors.Add("schedule section is missing.");
        else
        {
            if (!Enum.IsDefined(s.Mode)) errors.Add($"schedule mode '{s.Mode}' is unknown.");
            if (!TimeZoneResolver.TryResolve(s.TimeZone, out _)) errors.Add($"time zone '{s.TimeZone}' is unknown.");
            foreach (var period in s.Periods ?? Array.Empty<PeriodSpec>())
            {
                if (TryWindow(period, out _, out var error)) continue;
                errors.Add(error);
            }
        }

        if (p.Filtering is not null)
        {
            foreach (var d in p.Filtering.AiDomains.Concat(p.Filtering.BlockedDomains))
                if (DomainName.Normalize(d) is null) errors.Add($"'{d}' is not a valid domain name.");
        }

        if (p.Access is not null && !HotkeyGesture.TryParse(p.Access.Hotkey, out _, out var hotkeyError))
            errors.Add($"hotkey '{p.Access.Hotkey}': {hotkeyError}");

        return errors;
    }

    public static ClassSchedule ToSchedule(ClassroomPolicy p)
    {
        var windows = new List<ClassWindow>();
        foreach (var period in p.Schedule.Periods)
        {
            if (!TryWindow(period, out var w, out var error)) throw new InvalidDataException(error);
            windows.Add(w);
        }
        return new ClassSchedule(windows, TimeZoneResolver.Resolve(p.Schedule.TimeZone), p.Schedule.Mode);
    }

    /// <summary>All blocked domains, normalized and de-duplicated, AI first.</summary>
    public static IReadOnlyList<string> BlockedDomains(ClassroomPolicy p) =>
        p.Filtering.AiDomains.Concat(p.Filtering.BlockedDomains)
            .Select(DomainName.Normalize)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static bool TryWindow(PeriodSpec period, out ClassWindow window, out string error)
    {
        window = null!;
        error = "";
        if (period is null || !Enum.IsDefined(period.Day))
        {
            error = "a class period has no valid day.";
            return false;
        }
        if (!TryTime(period.Start, out var start) || !TryTime(period.End, out var end))
        {
            error = $"{period.Day} {period.Start}-{period.End}: times must be HH:mm.";
            return false;
        }
        if (end <= start)
        {
            error = $"{period.Day} {period.Start}-{period.End}: a period must end after it starts.";
            return false;
        }
        window = new ClassWindow(period.Day, start, end);
        return true;
    }

    private static bool TryTime(string? s, out TimeOnly t) =>
        TimeOnly.TryParseExact(s?.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
}
