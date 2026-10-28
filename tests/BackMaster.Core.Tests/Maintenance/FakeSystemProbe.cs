using BackMaster.Core.Maintenance;

namespace BackMaster.Core.Tests.Maintenance;

/// <summary>An in-memory machine that tests can damage in specific ways.</summary>
internal sealed class FakeSystemProbe : ISystemProbe
{
    public Dictionary<string, (long Size, string Hash)> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ServiceState> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TaskState> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Registry { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> InstalledProducts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int HashCalls { get; private set; }

    /// <summary>A machine that matches the manifest exactly.</summary>
    public static FakeSystemProbe HealthyFor(InstallManifest m)
    {
        var p = new FakeSystemProbe();
        foreach (var f in m.Files)
            p.Files[WinPath.Join(m.InstallDir, f.Path)] = (f.Size, f.Sha256);
        foreach (var s in m.Services)
            p.Services[s.Name] = new ServiceState(true, true, s.StartMode,
                $"\"{WinPath.Join(m.InstallDir, s.BinaryPath)}\" --service");
        foreach (var t in m.Tasks)
            p.Tasks[t.Name] = new TaskState(true, true);
        foreach (var r in m.Registry)
            p.Registry[$@"{r.Key}\{r.Name}"] = r.Value;
        p.InstalledProducts.Add(m.Installer.ProductCode);
        p.Files[m.Installer.CachedPath] = (1_000_000, m.Installer.Sha256);
        return p;
    }

    /// <summary>What happens when a student uninstalls BackMaster from Settings.</summary>
    public void SimulateUninstall(InstallManifest m)
    {
        foreach (var f in m.Files) Files.Remove(WinPath.Join(m.InstallDir, f.Path));
        Services.Clear();
        Tasks.Clear();
        InstalledProducts.Clear();
    }

    public FileState ProbeFile(string fullPath) =>
        Files.TryGetValue(fullPath, out var f) ? new FileState(true, f.Size) : FileState.Missing;

    public string HashFile(string fullPath)
    {
        HashCalls++;
        return Files[fullPath].Hash;
    }

    public ServiceState ProbeService(string name) =>
        Services.TryGetValue(name, out var s) ? s : ServiceState.Missing;

    public TaskState ProbeTask(string name) =>
        Tasks.TryGetValue(name, out var t) ? t : TaskState.Missing;

    public string? ReadRegistryValue(string key, string name) =>
        Registry.TryGetValue($@"{key}\{name}", out var v) ? v : null;

    public bool IsProductInstalled(string productCode) => InstalledProducts.Contains(productCode);
}

internal static class Sample
{
    public static string Hash(char c) => new(c, 64);

    public const string InstallDir = @"C:\Program Files\BackMaster";
    public const string Cache = @"C:\ProgramData\BackMaster\Installer\BackMaster.msi";
    public const string Share = @"\\TEACHER-PC\BackMaster\BackMaster.msi";
    public const string Usb = @"E:\BackMaster\BackMaster.msi";
    public const string ProductCode = "{6F1D2A7C-3B4E-4C5D-9E8F-0A1B2C3D4E5F}";
    public static readonly string InstallerHash = Hash('f');

    public static InstallManifest Manifest() => new(
        SchemaVersion: 1,
        ProductVersion: "0.1.0",
        InstallDir: InstallDir,
        Files: new[]
        {
            new ManifestFile("BackMaster.Service.exe", Hash('a'), 150_000, FileRole.Enforcement),
            new ManifestFile("BackMaster.Core.dll", Hash('b'), 80_000, FileRole.Enforcement),
            new ManifestFile("BackMaster.Tray.exe", Hash('c'), 120_000, FileRole.Interface),
            new ManifestFile(@"assets\companion\fox-companion.png", Hash('d'), 849_650, FileRole.Asset),
        },
        Services: new[] { new ServiceSpec("BackMaster", "BackMaster.Service.exe", ServiceStartMode.Automatic) },
        Tasks: new[] { new ScheduledTaskSpec(@"\BackMaster\Maintenance") },
        Registry: new[]
        {
            new RegistryValueSpec(@"HKLM\SOFTWARE\Policies\Google\Chrome", "DnsOverHttpsMode", "off", "Chrome secure DNS off"),
            new RegistryValueSpec(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "DnsOverHttpsMode", "off", "Edge secure DNS off"),
        },
        Installer: new InstallerSpec(ProductCode, Cache, InstallerHash, new[] { Share, Usb }));
}
