using BackMaster.Core.Companion;
using BackMaster.Core.Ipc;
using BackMaster.Core.Scheduling;
using Xunit;

namespace BackMaster.Core.Tests.Companion;

public class CompanionMessagesTests
{
    private static readonly CompanionMessages Messages = CompanionMessages.Load(RepoFile("assets/companion/messages.bg.json"));

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BackMaster.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, relative);
    }

    private static ServiceStatus Status(bool enforce, EnforcementMode mode, string? next) =>
        new(enforce, mode, next is null ? null : DateTimeOffset.Parse(next), "Europe/Sofia", 10, "");

    [Fact]
    public void During_a_lesson_the_tray_shows_the_end_in_Sofia_time()
    {
        // 10:30 UTC on 28 Oct 2026 is 12:30 in Sofia (UTC+2).
        var text = Messages.TrayText(Status(true, EnforcementMode.Scheduled, "2026-10-28T10:30:00Z"));
        Assert.Equal("Час е — ограниченията са включени до 12:30", text);
    }

    [Fact]
    public void Off_hours_the_tray_names_the_next_lesson_in_Bulgarian()
    {
        // 07:10 UTC on 29 Oct 2026 is Thursday 09:10 in Sofia.
        var text = Messages.TrayText(Status(false, EnforcementMode.Scheduled, "2026-10-29T07:10:00Z"));
        Assert.Equal("Извън час — свободно е. Следващ час: четвъртък 09:10", text);
    }

    [Fact]
    public void Administrator_modes_and_a_missing_service_have_their_own_texts()
    {
        Assert.Equal(Messages.TrayAlwaysOn, Messages.TrayText(Status(true, EnforcementMode.AlwaysOn, null)));
        Assert.Equal(Messages.TrayPaused, Messages.TrayText(Status(false, EnforcementMode.AlwaysOff, null)));
        Assert.Equal("Не мога да се свържа със службата на BackMaster", Messages.TrayText(null));
    }

    [Fact]
    public void Tray_texts_fit_the_notify_icon_limit()
    {
        var all = new[]
        {
            Messages.TrayText(Status(true, EnforcementMode.Scheduled, "2026-10-28T10:30:00Z")),
            Messages.TrayText(Status(false, EnforcementMode.Scheduled, "2026-10-29T07:10:00Z")),
            Messages.TrayAlwaysOn, Messages.TrayPaused, Messages.TrayUnreachable,
        };
        Assert.All(all, t => Assert.True(t.Length <= 127, t));
    }

    [Fact]
    public void The_lesson_start_message_fills_in_the_end_time()
    {
        var m = Messages.Starting(Status(true, EnforcementMode.Scheduled, "2026-10-28T10:30:00Z"));
        Assert.Equal("Започва час!", m.Title);
        Assert.Contains("до 12:30", m.Body);
        Assert.DoesNotContain("{", m.Body);
    }

    [Fact]
    public void Tips_carry_the_fox_prefix()
    {
        Assert.StartsWith("Съвет от лисицата: ", Messages.RandomTip(new Random(1)));
    }
}
