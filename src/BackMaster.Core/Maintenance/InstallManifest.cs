namespace BackMaster.Core.Maintenance;

/// <summary>
/// Describes what a healthy BackMaster installation looks like. The installer build
/// writes it next to the binaries; the maintenance tool compares the machine
/// against it.
///
/// A local administrator can edit this file like any other. It is a reference for
/// spotting accidental damage and casual tampering, not a security boundary: the
/// teacher dashboard, which notices when a PC stops reporting, covers the rest.
/// </summary>
public sealed record InstallManifest(
    int SchemaVersion,
    string ProductVersion,
    string InstallDir,
    IReadOnlyList<ManifestFile> Files,
    IReadOnlyList<ServiceSpec> Services,
    IReadOnlyList<ScheduledTaskSpec> Tasks,
    IReadOnlyList<RegistryValueSpec> Registry,
    InstallerSpec Installer)
{
    public const int CurrentSchemaVersion = 1;
}

/// <param name="Path">Relative to <see cref="InstallManifest.InstallDir"/>, backslash or slash separated.</param>
/// <param name="Sha256">Lowercase hex digest of the file as shipped.</param>
/// <param name="Role">What breaks if the file is lost; decides how serious its absence is.</param>
public sealed record ManifestFile(string Path, string Sha256, long Size, FileRole Role);

public enum FileRole
{
    /// <summary>Needed for enforcement (service, core library).</summary>
    Enforcement,
    /// <summary>Needed for the tray, the fox and the admin window.</summary>
    Interface,
    /// <summary>Images, message files and documentation.</summary>
    Asset,
}

public enum ServiceStartMode { Automatic, AutomaticDelayed, Manual, Disabled }

/// <param name="BinaryPath">Expected executable, relative to the install directory.</param>
public sealed record ServiceSpec(string Name, string BinaryPath, ServiceStartMode StartMode);

/// <summary>The scheduled task that runs the maintenance check independently of the service.</summary>
public sealed record ScheduledTaskSpec(string Name);

/// <summary>
/// A machine policy value BackMaster sets, such as a browser's DnsOverHttpsMode.
/// <paramref name="Key"/> is a full path starting with HKLM\.
/// </summary>
public sealed record RegistryValueSpec(string Key, string Name, string Value, string Purpose);

/// <param name="ProductCode">The MSI ProductCode GUID, with braces.</param>
/// <param name="CachedPath">Local copy of the original installer kept for repairs.</param>
/// <param name="Sha256">Digest of the original installer, used to verify every copy before running it.</param>
/// <param name="FallbackSources">Other places the same installer can be found, e.g. a classroom share or a USB stick.</param>
public sealed record InstallerSpec(
    string ProductCode,
    string CachedPath,
    string Sha256,
    IReadOnlyList<string> FallbackSources);
