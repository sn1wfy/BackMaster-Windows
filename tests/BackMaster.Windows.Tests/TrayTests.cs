using BackMaster.Core.Policy;
using BackMaster.Core.Scheduling;
using BackMaster.Platform;
using BackMaster.Tray;

namespace BackMaster.Windows.Tests;

public class TrayTests
{
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void The_embedded_fox_messages_load()
    {
        Assert.StartsWith("Час е", Messages.Load().TrayActive);
    }

    [Fact]
    public void Settings_form_shows_the_policy_and_reads_it_back_unchanged()
    {
        using var dir = new TempDir();
        var paths = new DataPaths(dir.Path, Path.Combine(dir.Path, "hosts"));
        var policy = ClassroomPolicy.Default with
        {
            Schedule = ClassroomPolicy.Default.Schedule with
            {
                Mode = EnforcementMode.AlwaysOn,
                Periods = new[] { new PeriodSpec(DayOfWeek.Wednesday, "11:00", "12:30"), new PeriodSpec(DayOfWeek.Thursday, "09:10", "10:50") },
            },
            Filtering = new FilteringSection(new[] { "chatgpt.com", "claude.ai" }, new[] { "poki.com" }),
            Access = new AccessSection("Ctrl+Alt+F9"),
        };
        PolicyStore.Apply(paths, new PolicyChange(policy, null), harden: false);

        RunSta(() =>
        {
            using var form = new SettingsForm(paths, Messages.Load());
            form.CreateControl();
            var errors = new List<string>();
            var read = form.ReadForm(errors);

            Assert.Empty(errors);
            Assert.Empty(PolicyLoader.Errors(read));
            Assert.Equal(EnforcementMode.AlwaysOn, read.Schedule.Mode);
            Assert.Equal(policy.Schedule.Periods, read.Schedule.Periods);
            Assert.Equal(policy.Filtering.AiDomains, read.Filtering.AiDomains);
            Assert.Equal(policy.Filtering.BlockedDomains, read.Filtering.BlockedDomains);
            Assert.Equal("Ctrl+Alt+F9", read.Access.Hotkey);
        });
    }
}
