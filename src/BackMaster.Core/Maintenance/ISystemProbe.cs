namespace BackMaster.Core.Maintenance;

/// <summary>
/// Read-only view of the machine. The Windows implementation lives in
/// BackMaster.Maintenance; tests use an in-memory fake.
/// </summary>
public interface ISystemProbe
{
    FileState ProbeFile(string fullPath);

    /// <summary>Lowercase hex SHA-256 of the file. Only called after <see cref="ProbeFile"/> found it.</summary>
    string HashFile(string fullPath);

    ServiceState ProbeService(string name);

    TaskState ProbeTask(string name);

    /// <summary>The value as a string, or null if the key or value does not exist.</summary>
    string? ReadRegistryValue(string key, string name);

    /// <summary>Whether Windows Installer lists the product as installed.</summary>
    bool IsProductInstalled(string productCode);
}

public readonly record struct FileState(bool Exists, long Size)
{
    public static FileState Missing => new(false, 0);
}

/// <param name="ImagePath">The service's command line as Windows stores it (may be quoted and have arguments).</param>
public readonly record struct ServiceState(bool Exists, bool Running, ServiceStartMode? StartMode, string? ImagePath)
{
    public static ServiceState Missing => new(false, false, null, null);
}

public readonly record struct TaskState(bool Exists, bool Enabled)
{
    public static TaskState Missing => new(false, false);
}
