using BackMaster.Core.Policy;
using BackMaster.Core.Scheduling;
using Xunit;

namespace BackMaster.Core.Tests.Policy;

public class PolicyLoaderTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BackMaster.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative);
    }

    [Fact]
    public void The_shipped_default_policy_loads_and_matches_the_built_in_default()
    {
        var p = PolicyLoader.Load(RepoFile(@"config/policy.default.json"));
        Assert.Equal(EnforcementMode.Scheduled, p.Schedule.Mode);
        Assert.Equal("Europe/Sofia", p.Schedule.TimeZone);
        Assert.Equal(ClassroomPolicy.Default.Schedule.Periods, p.Schedule.Periods);
        Assert.Equal(ClassroomPolicy.Default.Filtering.AiDomains, p.Filtering.AiDomains);
        Assert.Equal(ClassroomPolicy.Default.Filtering.BlockedDomains, p.Filtering.BlockedDomains);
        Assert.Equal("Ctrl+Alt+Shift+B", p.Access.Hotkey);
    }

    [Fact]
    public void The_shipped_default_policy_enforces_the_real_lessons()
    {
        var schedule = PolicyLoader.ToSchedule(PolicyLoader.Load(RepoFile(@"config/policy.default.json")));
        Assert.True(schedule.Evaluate(DateTimeOffset.Parse("2026-10-28T11:30:00+02:00")).Enforce);  // Wednesday
        Assert.True(schedule.Evaluate(DateTimeOffset.Parse("2026-10-29T09:10:00+02:00")).Enforce);  // Thursday
        Assert.False(schedule.Evaluate(DateTimeOffset.Parse("2026-10-29T10:50:00+02:00")).Enforce);
    }

    [Fact]
    public void A_schedule_only_policy_gets_default_filtering_and_hotkey()
    {
        var p = PolicyLoader.Parse("""
            { "schemaVersion": 1,
              "schedule": { "mode": "Scheduled", "timeZone": "Europe/Sofia",
                            "periods": [ { "day": "Wednesday", "start": "11:00", "end": "12:30" } ] } }
            """);
        Assert.Equal(FilteringSection.Default, p.Filtering);
        Assert.Equal(AccessSection.Default, p.Access);
    }

    [Fact]
    public void Serialize_then_parse_round_trips()
    {
        var original = ClassroomPolicy.Default with
        {
            Access = new AccessSection("Ctrl+Alt+F9"),
            Filtering = new FilteringSection(new[] { "chatgpt.com" }, new[] { "poki.com" }),
        };
        var copy = PolicyLoader.Parse(PolicyLoader.Serialize(original));
        Assert.Equal(original.Schedule.Mode, copy.Schedule.Mode);
        Assert.Equal(original.Schedule.Periods, copy.Schedule.Periods);
        Assert.Equal(original.Filtering.AiDomains, copy.Filtering.AiDomains);
        Assert.Equal("Ctrl+Alt+F9", copy.Access.Hotkey);
        Assert.Contains("\"mode\": \"Scheduled\"", PolicyLoader.Serialize(original));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var ex = Assert.Throws<InvalidDataException>(() => PolicyLoader.Parse("""
            { "schemaVersion": 2,
              "schedule": { "mode": "Scheduled", "timeZone": "Mars/Olympus",
                            "periods": [ { "day": "Wednesday", "start": "12:30", "end": "11:00" },
                                         { "day": "Thursday", "start": "9.10", "end": "10:50" } ] },
              "filtering": { "aiDomains": [ "not a domain" ], "blockedDomains": [] },
              "access": { "hotkey": "B" } }
            """));
        Assert.Contains("schemaVersion 2", ex.Message);
        Assert.Contains("Mars/Olympus", ex.Message);
        Assert.Contains("must end after it starts", ex.Message);
        Assert.Contains("HH:mm", ex.Message);
        Assert.Contains("'not a domain'", ex.Message);
        Assert.Contains("hotkey 'B'", ex.Message);
    }

    [Fact]
    public void Broken_json_is_rejected_with_a_clear_message()
    {
        var ex = Assert.Throws<InvalidDataException>(() => PolicyLoader.Parse("{ \"schemaVersion\": "));
        Assert.StartsWith("policy.json is not valid JSON", ex.Message);
    }

    [Fact]
    public void Blocked_domains_are_normalized_and_deduplicated()
    {
        var p = ClassroomPolicy.Default with
        {
            Filtering = new FilteringSection(
                new[] { "https://www.ChatGPT.com/c/1", "chatgpt.com" },
                new[] { "Poki.com", "chatgpt.com" }),
        };
        Assert.Equal(new[] { "chatgpt.com", "poki.com" }, PolicyLoader.BlockedDomains(p));
    }
}
