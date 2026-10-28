namespace BackMaster.Core.Maintenance;

public enum HealthStatus { Healthy, Degraded, Broken }

/// <summary>
/// Critical: enforcement is not working right now.
/// Warning: something is damaged or will stop working (e.g. after a reboot).
/// </summary>
public enum IssueSeverity { Warning, Critical }

public enum IssueKind
{
    FileMissing,
    FileModified,
    ServiceMissing,
    ServiceStopped,
    ServiceStartModeChanged,
    ServiceBinaryChanged,
    TaskMissing,
    TaskDisabled,
    RegistryValueMissing,
    RegistryValueChanged,
    ProductNotRegistered,
    InstallerCacheMissing,
    InstallerCacheModified,
}

/// <param name="Subject">What the issue is about: a relative file path, service or task name, or registry path.</param>
/// <param name="Detail">A short English sentence for the log and the admin window.</param>
public sealed record HealthIssue(IssueKind Kind, IssueSeverity Severity, string Subject, string Detail);

public sealed record HealthReport(
    HealthStatus Status,
    IReadOnlyList<HealthIssue> Issues,
    DateTimeOffset CheckedAt,
    string ProductVersion)
{
    public bool Has(IssueKind kind) => Issues.Any(i => i.Kind == kind);
}

/// <summary>Compares the machine against the install manifest.</summary>
public sealed class HealthChecker
{
    private readonly ISystemProbe _probe;

    public HealthChecker(ISystemProbe probe) => _probe = probe ?? throw new ArgumentNullException(nameof(probe));

    public HealthReport Check(InstallManifest m, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(m);
        var issues = new List<HealthIssue>();

        CheckFiles(m, issues);
        CheckServices(m, issues);
        CheckTasks(m, issues);
        CheckRegistry(m, issues);
        CheckInstaller(m, issues);

        var status = issues.Any(i => i.Severity == IssueSeverity.Critical) ? HealthStatus.Broken
                   : issues.Count > 0 ? HealthStatus.Degraded
                   : HealthStatus.Healthy;
        return new HealthReport(status, issues, now, m.ProductVersion);
    }

    private void CheckFiles(InstallManifest m, List<HealthIssue> issues)
    {
        foreach (var f in m.Files)
        {
            string rel = ManifestLoader.NormalizeRelative(f.Path);
            string full = WinPath.Join(m.InstallDir, rel);
            var severity = f.Role == FileRole.Enforcement ? IssueSeverity.Critical : IssueSeverity.Warning;

            var state = _probe.ProbeFile(full);
            if (!state.Exists)
            {
                issues.Add(new(IssueKind.FileMissing, severity, rel, $"{rel} is missing."));
                continue;
            }
            // A size mismatch is already proof of change; skip hashing it.
            if (state.Size != f.Size || !string.Equals(_probe.HashFile(full), f.Sha256, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(IssueKind.FileModified, severity, rel, $"{rel} differs from the installed version."));
        }
    }

    private void CheckServices(InstallManifest m, List<HealthIssue> issues)
    {
        foreach (var s in m.Services)
        {
            var state = _probe.ProbeService(s.Name);
            if (!state.Exists)
            {
                issues.Add(new(IssueKind.ServiceMissing, IssueSeverity.Critical, s.Name, $"The {s.Name} service is not installed."));
                continue;
            }

            string expectedExe = WinPath.Join(m.InstallDir, ManifestLoader.NormalizeRelative(s.BinaryPath));
            string? actualExe = WinPath.ExecutableFromCommandLine(state.ImagePath);
            if (actualExe is null || !string.Equals(actualExe, expectedExe, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(IssueKind.ServiceBinaryChanged, IssueSeverity.Critical, s.Name,
                    $"The {s.Name} service runs '{actualExe ?? "(nothing)"}' instead of '{expectedExe}'."));

            if (!state.Running)
                issues.Add(new(IssueKind.ServiceStopped, IssueSeverity.Critical, s.Name, $"The {s.Name} service is not running."));

            if (state.StartMode != s.StartMode)
                issues.Add(new(IssueKind.ServiceStartModeChanged,
                    state.StartMode == ServiceStartMode.Disabled ? IssueSeverity.Critical : IssueSeverity.Warning,
                    s.Name, $"The {s.Name} service start type is {state.StartMode?.ToString() ?? "unknown"}, expected {s.StartMode}."));
        }
    }

    private void CheckTasks(InstallManifest m, List<HealthIssue> issues)
    {
        foreach (var t in m.Tasks)
        {
            var state = _probe.ProbeTask(t.Name);
            if (!state.Exists)
                issues.Add(new(IssueKind.TaskMissing, IssueSeverity.Warning, t.Name, $"The scheduled task {t.Name} is missing."));
            else if (!state.Enabled)
                issues.Add(new(IssueKind.TaskDisabled, IssueSeverity.Warning, t.Name, $"The scheduled task {t.Name} is disabled."));
        }
    }

    private void CheckRegistry(InstallManifest m, List<HealthIssue> issues)
    {
        foreach (var r in m.Registry)
        {
            string subject = $@"{r.Key}\{r.Name}";
            string? actual = _probe.ReadRegistryValue(r.Key, r.Name);
            if (actual is null)
                issues.Add(new(IssueKind.RegistryValueMissing, IssueSeverity.Warning, subject, $"{r.Purpose}: the policy value is missing."));
            else if (!string.Equals(actual, r.Value, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(IssueKind.RegistryValueChanged, IssueSeverity.Warning, subject, $"{r.Purpose}: the policy is '{actual}', expected '{r.Value}'."));
        }
    }

    private void CheckInstaller(InstallManifest m, List<HealthIssue> issues)
    {
        var inst = m.Installer;
        if (!_probe.IsProductInstalled(inst.ProductCode))
            issues.Add(new(IssueKind.ProductNotRegistered, IssueSeverity.Critical, inst.ProductCode,
                "BackMaster is not registered with Windows Installer (it may have been uninstalled)."));

        var cache = _probe.ProbeFile(inst.CachedPath);
        if (!cache.Exists)
            issues.Add(new(IssueKind.InstallerCacheMissing, IssueSeverity.Warning, inst.CachedPath,
                "The saved copy of the installer is missing, so automatic repair needs another source."));
        else if (!string.Equals(_probe.HashFile(inst.CachedPath), inst.Sha256, StringComparison.OrdinalIgnoreCase))
            issues.Add(new(IssueKind.InstallerCacheModified, IssueSeverity.Warning, inst.CachedPath,
                "The saved copy of the installer does not match the original and will not be used."));
    }
}
