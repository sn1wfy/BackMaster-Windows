using BackMaster.Core.Maintenance;
using Xunit;

namespace BackMaster.Core.Tests.Maintenance;

public class ManifestLoaderTests
{
    [Fact]
    public void A_manifest_survives_a_round_trip()
    {
        string json = ManifestLoader.Serialize(Sample.Manifest());
        Assert.Contains("\"role\": \"Enforcement\"", json);       // enums are written as names
        Assert.Contains("\"startMode\": \"Automatic\"", json);
        Assert.Equal(json, ManifestLoader.Serialize(ManifestLoader.Parse(json)));
    }

    [Theory]
    [InlineData("files", @"..\..\Windows\System32\evil.dll", "must be relative")]
    [InlineData("files", @"C:\Windows\notepad.exe", "must be relative")]
    [InlineData("sha", "not-a-hash", "invalid sha256")]
    [InlineData("productCode", "6F1D2A7C", "GUID in braces")]
    [InlineData("registry", @"HKCU\Software\X", "must start with HKLM")]
    [InlineData("cache", @"C:\ProgramData\BackMaster\setup.exe", "path to an .msi")]
    [InlineData("schema", "2", "schemaVersion 2 is not supported")]
    public void Unsafe_or_malformed_manifests_are_rejected(string field, string value, string expectedMessage)
    {
        var m = Sample.Manifest();
        m = field switch
        {
            "files" => m with { Files = new[] { m.Files[0] with { Path = value } } },
            "sha" => m with { Files = new[] { m.Files[0] with { Sha256 = value } } },
            "productCode" => m with { Installer = m.Installer with { ProductCode = value } },
            "registry" => m with { Registry = new[] { m.Registry[0] with { Key = value } } },
            "cache" => m with { Installer = m.Installer with { CachedPath = value } },
            "schema" => m with { SchemaVersion = int.Parse(value) },
            _ => throw new ArgumentException(field),
        };
        var e = Assert.Throws<InvalidDataException>(() => ManifestLoader.Parse(ManifestLoader.Serialize(m)));
        Assert.Contains(expectedMessage, e.Message);
    }

    [Fact]
    public void A_file_listed_twice_is_rejected()
    {
        var m = Sample.Manifest();
        m = m with { Files = new[] { m.Files[0], m.Files[0] with { Path = "backmaster.service.exe" } } };
        var e = Assert.Throws<InvalidDataException>(() => ManifestLoader.Validate(m));
        Assert.Contains("listed twice", e.Message);
    }

    [Fact]
    public void Broken_json_gives_a_clear_error()
    {
        var e = Assert.Throws<InvalidDataException>(() => ManifestLoader.Parse("{ \"schemaVersion\": "));
        Assert.Contains("not valid JSON", e.Message);
    }
}

public class AlertGateTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-28T11:00:00+02:00");
    private readonly InstallManifest _m = Sample.Manifest();
    private readonly AlertGate _gate = new(TimeSpan.FromHours(12));

    private HealthReport Report(Action<FakeSystemProbe>? damage = null)
    {
        var p = FakeSystemProbe.HealthyFor(_m);
        damage?.Invoke(p);
        return new HealthChecker(p).Check(_m, T0);
    }

    private static void StopService(FakeSystemProbe p) =>
        p.Services["BackMaster"] = p.Services["BackMaster"] with { Running = false };

    [Fact]
    public void Healthy_checks_stay_quiet()
    {
        var d = _gate.Decide(AlertState.Initial, Report(), T0);
        Assert.Equal(AlertKind.None, d.Kind);
    }

    [Fact]
    public void A_new_problem_alerts_once_then_reminds_after_the_interval()
    {
        var broken = Report(StopService);
        var first = _gate.Decide(AlertState.Initial, broken, T0);
        Assert.Equal(AlertKind.Problem, first.Kind);

        var again = _gate.Decide(first.NextState, broken, T0.AddMinutes(15));
        Assert.Equal(AlertKind.None, again.Kind);

        var later = _gate.Decide(again.NextState, broken, T0.AddHours(12));
        Assert.Equal(AlertKind.Reminder, later.Kind);
        Assert.Equal(T0.AddHours(12), later.NextState.LastAlertAt);
    }

    [Fact]
    public void A_different_problem_alerts_straight_away()
    {
        var first = _gate.Decide(AlertState.Initial, Report(StopService), T0);
        var other = Report(p => p.Tasks.Clear());
        Assert.Equal(AlertKind.Problem, _gate.Decide(first.NextState, other, T0.AddMinutes(15)).Kind);
    }

    [Fact]
    public void Getting_worse_alerts_straight_away()
    {
        var degraded = _gate.Decide(AlertState.Initial, Report(p => p.Tasks.Clear()), T0);
        Assert.Equal(HealthStatus.Degraded, degraded.NextState.LastStatus);
        var worse = Report(p => { p.Tasks.Clear(); StopService(p); });
        Assert.Equal(AlertKind.Problem, _gate.Decide(degraded.NextState, worse, T0.AddMinutes(15)).Kind);
    }

    [Fact]
    public void Recovery_is_announced_once()
    {
        var broken = _gate.Decide(AlertState.Initial, Report(StopService), T0);
        var fixedNow = _gate.Decide(broken.NextState, Report(), T0.AddMinutes(15));
        Assert.Equal(AlertKind.Recovered, fixedNow.Kind);
        Assert.Equal(AlertKind.None, _gate.Decide(fixedNow.NextState, Report(), T0.AddMinutes(30)).Kind);
    }

    [Fact]
    public void The_reminder_interval_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlertGate(TimeSpan.Zero));
    }
}
