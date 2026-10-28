namespace BackMaster.Platform;

/// <summary>
/// Where BackMaster keeps machine-wide state. The service can be pointed elsewhere for
/// testing; the tray and the elevated helper always use the real locations.
/// </summary>
public sealed record DataPaths(string DataDir, string HostsFile)
{
    public static DataPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BackMaster"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts"));

    /// <summary>Readable by all users, writable by Administrators.</summary>
    public string PolicyFile => Path.Combine(DataDir, "policy.json");

    /// <summary>Only SYSTEM and Administrators can read anything in here.</summary>
    public string PrivateDir => Path.Combine(DataDir, "private");

    public string VerifierFile => Path.Combine(PrivateDir, "unlock.verifier");

    /// <summary>The default policy shipped next to the executables.</summary>
    public static string ShippedDefaultPolicy => Path.Combine(AppContext.BaseDirectory, "config", "policy.default.json");
}
