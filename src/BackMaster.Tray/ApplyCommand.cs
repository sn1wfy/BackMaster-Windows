using BackMaster.Platform;

namespace BackMaster.Tray;

/// <summary>
/// The elevated half of saving settings. Runs only after the UAC prompt, re-validates
/// everything it was handed, and writes it to %ProgramData%\BackMaster. The service
/// notices the new policy.json within a few seconds.
/// </summary>
internal static class ApplyCommand
{
    public const int Success = 0;
    public const int NotElevated = 2;
    public const int Rejected = 3;
    public const int Failed = 4;

    public static int Run(string file, string sha256)
    {
        if (!Elevation.IsElevated) return NotElevated;
        try
        {
            var change = PolicyStore.DecodeVerified(File.ReadAllText(file), sha256);
            PolicyStore.Apply(DataPaths.Default, change);
            return Success;
        }
        catch (InvalidDataException e)
        {
            MessageBox.Show(e.Message, "BackMaster", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return Rejected;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Настройките не можаха да бъдат записани:\n" + e.Message, "BackMaster", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return Failed;
        }
    }

    /// <summary>
    /// Saves a change: directly when already elevated, otherwise through an elevated copy
    /// of this program. Returns null on success or the reason it did not happen.
    /// </summary>
    public static string? Save(PolicyChange change)
    {
        if (Elevation.IsElevated)
        {
            PolicyStore.Apply(DataPaths.Default, change);
            return null;
        }

        string payload = PolicyStore.Encode(change);
        string file = Path.Combine(Path.GetTempPath(), $"BackMaster-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, payload);
        try
        {
            return Elevation.RunSelfElevated(new[] { "--apply", file, PolicyStore.Hash(payload) }) switch
            {
                null => "Записът е отказан в прозореца за администраторско разрешение.",
                Success => null,
                NotElevated => "Записът изисква администраторски права.",
                _ => "Настройките не бяха записани.",
            };
        }
        finally
        {
            File.Delete(file);
        }
    }
}
