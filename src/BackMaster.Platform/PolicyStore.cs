using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackMaster.Core.Policy;
using BackMaster.Core.Security;

namespace BackMaster.Platform;

/// <summary>What the settings window asks the elevated helper to write.</summary>
/// <param name="Verifier">A new phrase verifier, or null to keep the current phrase.</param>
public sealed record PolicyChange(ClassroomPolicy Policy, string? Verifier);

/// <summary>Loading and saving the policy and the phrase verifier.</summary>
public static class PolicyStore
{
    /// <summary>The installed policy, else the shipped default, else the built-in default.</summary>
    public static ClassroomPolicy LoadOrDefault(DataPaths paths)
    {
        if (File.Exists(paths.PolicyFile)) return PolicyLoader.Load(paths.PolicyFile);
        if (File.Exists(DataPaths.ShippedDefaultPolicy)) return PolicyLoader.Load(DataPaths.ShippedDefaultPolicy);
        return ClassroomPolicy.Default;
    }

    public static string? ReadVerifier(DataPaths paths)
    {
        try
        {
            return File.Exists(paths.VerifierFile) ? File.ReadAllText(paths.VerifierFile).Trim() : null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes a validated change. Must run elevated (or as SYSTEM) on a real installation.</summary>
    public static void Apply(DataPaths paths, PolicyChange change, bool harden = true)
    {
        PolicyLoader.Validate(change.Policy);
        if (change.Verifier is not null && !PhraseVerifier.IsWellFormed(change.Verifier))
            throw new InvalidDataException("The new phrase verifier is malformed.");

        if (harden) SecureFiles.EnsureDataDirectories(paths);
        else Directory.CreateDirectory(paths.PrivateDir);
        SecureFiles.WriteAtomic(paths.PolicyFile, PolicyLoader.Serialize(change.Policy));
        if (change.Verifier is not null) SecureFiles.WriteAtomic(paths.VerifierFile, change.Verifier);
    }

    // The change travels to the elevated helper through a file in the user's temp folder.
    // Its SHA-256 goes on the helper's command line, which the user cannot alter after
    // approving the UAC prompt, so swapping the file in between is detected.

    public static string Encode(PolicyChange change) => JsonSerializer.Serialize(change, PolicyLoader.JsonOptions);

    public static string Hash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    public static PolicyChange DecodeVerified(string payload, string expectedSha256)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(payload)), Encoding.ASCII.GetBytes(expectedSha256.ToLowerInvariant())))
            throw new InvalidDataException("The settings file changed after it was handed over; nothing was saved.");
        var change = JsonSerializer.Deserialize<PolicyChange>(payload, PolicyLoader.JsonOptions)
                     ?? throw new InvalidDataException("The settings file is empty.");
        // Round-trip through the loader so missing sections get the same defaults as on disk.
        return change with { Policy = PolicyLoader.Parse(PolicyLoader.Serialize(change.Policy)) };
    }
}
