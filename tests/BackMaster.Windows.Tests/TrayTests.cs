using BackMaster.Core.Filtering;
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

    [Theory]
    [InlineData("chatgpt.com", true)]
    [InlineData("poki.com", false)]
    public void Fox_card_renders_blocked_messages(string domain, bool isAi)
    {
        var messages = Messages.Load();
        var card = FoxCard.Blocked(messages.Blocked(new BlockedAttempt(1, domain, isAi, DateTimeOffset.UtcNow)), isAi, messages.RandomTip(new Random(2)));
        using var rendered = FoxCardRenderer.Render(card, messages.Dismiss, 1f);

        Assert.InRange(rendered.Bitmap.Width, 700, 800);
        Assert.InRange(rendered.Bitmap.Height, 330, 500);
        // Transparent outside the panel, opaque inside, and the fox's ear above the panel.
        Assert.Equal(0, rendered.Bitmap.GetPixel(rendered.Bitmap.Width - 2, 2).A);
        Assert.Equal(255, rendered.Bitmap.GetPixel(rendered.Bitmap.Width / 2, rendered.Bitmap.Height / 2).A);
        Assert.True(Enumerable.Range(30, 120).Any(x => rendered.Bitmap.GetPixel(x, 30).A > 200), "fox ears should rise above the panel");
        Assert.True(rendered.DismissButton.Right <= rendered.Bitmap.Width && rendered.DismissButton.Width > 40);
        Assert.True(rendered.CloseButton.Width > 10);

        // Saved so a person can look at the layout; set BACKMASTER_FOX_DIR to choose where.
        string dir = Environment.GetEnvironmentVariable("BACKMASTER_FOX_DIR") ?? Path.GetTempPath();
        rendered.Bitmap.Save(Path.Combine(dir, $"backmaster-fox-{domain}.png"));
    }

    [Fact]
    public void Lesson_and_tip_cards_render()
    {
        var messages = Messages.Load();
        var status = new BackMaster.Core.Ipc.ServiceStatus(true, EnforcementMode.Scheduled, DateTimeOffset.UtcNow.AddMinutes(90), "Europe/Sofia", 36, "");
        using var start = FoxCardRenderer.Render(FoxCard.Lesson("ЧАС", messages.Starting(status), messages.RandomTip(new Random(1))), messages.Dismiss, 1.5f);
        using var tip = FoxCardRenderer.Render(new FoxCard(FoxTone.Tip, "СЪВЕТ", "Съвет от лисицата", "Раздели задачата на малки стъпки."), messages.Dismiss, 1f);
        Assert.True(start.Bitmap.Width > tip.Bitmap.Width); // scaled for high DPI
        string dir = Environment.GetEnvironmentVariable("BACKMASTER_FOX_DIR") ?? Path.GetTempPath();
        start.Bitmap.Save(Path.Combine(dir, "backmaster-fox-lesson.png"));
    }

    [Fact]
    public void Tray_icons_show_the_fox_with_a_status_dot()
    {
        string dir = Environment.GetEnvironmentVariable("BACKMASTER_FOX_DIR") ?? Path.GetTempPath();
        using var strip = new Bitmap(32 * 3 * 4, 32 * 4);
        using (var g = Graphics.FromImage(strip))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            int i = 0;
            foreach (var icon in new[] { TrayIcons.Lesson, TrayIcons.Free, TrayIcons.Unknown })
                g.DrawImage(icon.ToBitmap(), new Rectangle(i++ * 128, 0, 128, 128));
        }
        strip.Save(Path.Combine(dir, "backmaster-tray-icons.png"));
        Assert.NotEqual(TrayIcons.Lesson.ToBitmap().GetPixel(25, 25), TrayIcons.Free.ToBitmap().GetPixel(25, 25));
    }
}
