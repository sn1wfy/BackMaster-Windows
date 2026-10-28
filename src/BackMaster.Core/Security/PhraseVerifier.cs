using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BackMaster.Core.Security;

/// <summary>
/// Creates and checks the stored verifier for the settings unlock phrase. Only a salted
/// PBKDF2-HMAC-SHA256 hash is stored ("pbkdf2-sha256$iterations$salt$hash", base64),
/// never the phrase. The verifier file is readable only by SYSTEM and Administrators and
/// the service rate-limits guesses, so the hash is a second line of defence, not the first.
/// </summary>
public static class PhraseVerifier
{
    public const string Algorithm = "pbkdf2-sha256";

    /// <summary>OWASP's 2023 recommendation for PBKDF2-HMAC-SHA256; about 0.2-0.5 s on a classroom PC.</summary>
    public const int DefaultIterations = 600_000;

    public const int MinimumPhraseLength = 8;

    // Bounds for stored values, so a corrupted file cannot make the service hash for minutes.
    private const int MinIterations = 100_000;
    private const int MaxIterations = 10_000_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Why a new phrase is unacceptable, or null if it is fine.</summary>
    public static string? Weakness(string? phrase)
    {
        string p = Normalize(phrase ?? "");
        if (p.Length < MinimumPhraseLength) return $"Фразата трябва да е поне {MinimumPhraseLength} знака.";
        if (p.Distinct().Count() < 4) return "Фразата е твърде лесна за отгатване.";
        return null;
    }

    public static string Create(string phrase, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (Weakness(phrase) is { } why) throw new ArgumentException(why, nameof(phrase));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Derive(phrase, salt, iterations);
        return string.Join('$', Algorithm, iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>True only for a well-formed verifier made from the same phrase. Never throws on bad input.</summary>
    public static bool Verify(string? phrase, string? verifier)
    {
        if (phrase is null || !TryParse(verifier, out int iterations, out byte[] salt, out byte[] expected))
            return false;
        byte[] actual = Derive(phrase, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool IsWellFormed(string? verifier) => TryParse(verifier, out _, out _, out _);

    /// <summary>
    /// Trims and applies Unicode NFC, so the same phrase typed on a Bulgarian Phonetic or
    /// BDS layout, or pasted, produces the same bytes. Case is significant.
    /// </summary>
    public static string Normalize(string phrase) => phrase.Trim().Normalize(NormalizationForm.FormC);

    private static byte[] Derive(string phrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Normalize(phrase)), salt, iterations, HashAlgorithmName.SHA256, HashBytes);

    private static bool TryParse(string? verifier, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = hash = Array.Empty<byte>();
        var parts = verifier?.Trim().Split('$');
        if (parts is not { Length: 4 } || parts[0] != Algorithm) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations < MinIterations || iterations > MaxIterations) return false;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        return salt.Length >= SaltBytes && hash.Length == HashBytes;
    }
}
