using BackMaster.Core.Maintenance;
using Xunit;

namespace BackMaster.Core.Tests.Maintenance;

public class HealthCheckerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-28T11:05:00+02:00");
    private readonly InstallManifest _m = Sample.Manifest();

    private HealthReport Check(FakeSystemProbe p) => new HealthChecker(p).Check(_m, Now);

    private static string Kinds(HealthReport r) => string.Join(",", r.Issues.Select(i => i.Kind));

    [Fact]
    public void A_matching_machine_is_healthy()
    {
        var r = Check(FakeSystemProbe.HealthyFor(_m));
        Assert.Equal(HealthStatus.Healthy, r.Status);
        Assert.Equal(0, r.Issues.Count);
        Assert.Equal("0.1.0", r.ProductVersion);
    }

    [Fact]
    public void A_missing_enforcement_file_is_critical()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Files.Remove(@"C:\Program Files\BackMaster\BackMaster.Service.exe");
        var r = Check(p);
        Assert.Equal(HealthStatus.Broken, r.Status);
        var issue = Assert.Single(r.Issues);
        Assert.Equal(IssueKind.FileMissing, issue.Kind);
        Assert.Equal(IssueSeverity.Critical, issue.Severity);
        Assert.Equal("BackMaster.Service.exe", issue.Subject);
    }

    [Fact]
    public void A_missing_asset_only_degrades()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Files.Remove(@"C:\Program Files\BackMaster\assets\companion\fox-companion.png");
        var r = Check(p);
        Assert.Equal(HealthStatus.Degraded, r.Status);
        Assert.Equal(@"assets\companion\fox-companion.png", Assert.Single(r.Issues).Subject);
    }

    [Fact]
    public void A_file_with_the_same_size_but_different_content_is_modified()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Files[@"C:\Program Files\BackMaster\BackMaster.Core.dll"] = (80_000, Sample.Hash('9'));
        Assert.Equal("FileModified", Kinds(Check(p)));
    }

    [Fact]
    public void A_size_change_is_detected_without_hashing_the_file()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Files[@"C:\Program Files\BackMaster\BackMaster.Tray.exe"] = (5, Sample.Hash('c'));
        var r = Check(p);
        Assert.Equal("FileModified", Kinds(r));
        // Three intact files and the installer cache are hashed; the resized file is not.
        Assert.Equal(4, p.HashCalls);
    }

    [Fact]
    public void A_deleted_service_is_critical()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Services.Clear();
        var r = Check(p);
        Assert.Equal(HealthStatus.Broken, r.Status);
        Assert.Equal("ServiceMissing", Kinds(r));
    }

    [Fact]
    public void A_stopped_service_is_critical()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Services["BackMaster"] = p.Services["BackMaster"] with { Running = false };
        var r = Check(p);
        Assert.Equal(HealthStatus.Broken, r.Status);
        Assert.Equal("ServiceStopped", Kinds(r));
    }

    [Theory]
    [InlineData(ServiceStartMode.Manual, IssueSeverity.Warning)]
    [InlineData(ServiceStartMode.Disabled, IssueSeverity.Critical)]
    public void A_changed_start_type_is_reported(ServiceStartMode actual, IssueSeverity expected)
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Services["BackMaster"] = p.Services["BackMaster"] with { StartMode = actual };
        var issue = Assert.Single(Check(p).Issues);
        Assert.Equal(IssueKind.ServiceStartModeChanged, issue.Kind);
        Assert.Equal(expected, issue.Severity);
    }

    [Theory]
    [InlineData(@"C:\Users\student\fake.exe", true)]
    [InlineData(@"""C:\Program Files\BackMaster\BackMaster.Service.exe""", false)]
    [InlineData(@"""c:\program files\backmaster\backmaster.service.exe"" --service --verbose", false)]
    [InlineData(@"C:\Program Files\BackMaster\BackMaster.Service.exe --service", false)]
    public void The_service_must_run_the_installed_binary(string imagePath, bool flagged)
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Services["BackMaster"] = p.Services["BackMaster"] with { ImagePath = imagePath };
        Assert.Equal(flagged, Check(p).Has(IssueKind.ServiceBinaryChanged));
    }

    [Fact]
    public void A_missing_or_disabled_maintenance_task_is_a_warning()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Tasks[@"\BackMaster\Maintenance"] = new TaskState(true, false);
        var r = Check(p);
        Assert.Equal(HealthStatus.Degraded, r.Status);
        Assert.Equal("TaskDisabled", Kinds(r));

        p.Tasks.Clear();
        Assert.Equal("TaskMissing", Kinds(Check(p)));
    }

    [Fact]
    public void Edited_or_deleted_browser_policies_are_reported()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Registry[@"HKLM\SOFTWARE\Policies\Google\Chrome\DnsOverHttpsMode"] = "secure";
        p.Registry.Remove(@"HKLM\SOFTWARE\Policies\Microsoft\Edge\DnsOverHttpsMode");
        var r = Check(p);
        Assert.Equal(HealthStatus.Degraded, r.Status);
        Assert.Equal("RegistryValueChanged,RegistryValueMissing", Kinds(r));
    }

    [Fact]
    public void Policy_values_compare_case_insensitively()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Registry[@"HKLM\SOFTWARE\Policies\Google\Chrome\DnsOverHttpsMode"] = "OFF";
        Assert.Equal(HealthStatus.Healthy, Check(p).Status);
    }

    [Fact]
    public void An_uninstall_from_Settings_is_broken_on_every_count()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.SimulateUninstall(_m);
        var r = Check(p);
        Assert.Equal(HealthStatus.Broken, r.Status);
        Assert.True(r.Has(IssueKind.ProductNotRegistered));
        Assert.True(r.Has(IssueKind.ServiceMissing));
        Assert.True(r.Has(IssueKind.TaskMissing));
        Assert.Equal(4, r.Issues.Count(i => i.Kind == IssueKind.FileMissing));
    }

    [Fact]
    public void A_missing_or_altered_installer_copy_is_a_warning()
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        p.Files[Sample.Cache] = (1_000_000, Sample.Hash('0'));
        var r = Check(p);
        Assert.Equal(HealthStatus.Degraded, r.Status);
        Assert.Equal("InstallerCacheModified", Kinds(r));

        p.Files.Remove(Sample.Cache);
        Assert.Equal("InstallerCacheMissing", Kinds(Check(p)));
    }
}

public class WinPathTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\BackMaster\svc.exe"" --a", @"C:\Program Files\BackMaster\svc.exe")]
    [InlineData(@"C:\Program Files\BackMaster\svc.exe --a", @"C:\Program Files\BackMaster\svc.exe")]
    [InlineData(@"C:\tools\svc", @"C:\tools\svc")]
    [InlineData(@"""unterminated", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Extracts_the_executable_from_a_service_command_line(string? commandLine, string? expected)
    {
        Assert.Equal(expected, WinPath.ExecutableFromCommandLine(commandLine));
    }

    [Theory]
    [InlineData(@"C:\Program Files\BackMaster", "a/b.png", @"C:\Program Files\BackMaster\a\b.png")]
    [InlineData(@"C:\Program Files\BackMaster\", @"\x.exe", @"C:\Program Files\BackMaster\x.exe")]
    public void Joins_windows_paths_on_any_os(string dir, string rel, string expected)
    {
        Assert.Equal(expected, WinPath.Join(dir, rel));
    }
}
