using System.Globalization;
using System.Text.RegularExpressions;

namespace BackMaster.Core.Filtering;

public static partial class DomainName
{
    private static readonly IdnMapping Idn = new();

    /// <summary>
    /// Turns what a teacher types ("https://www.ChatGPT.com/c/123", "Пример.бг") into a
    /// lowercase ASCII host name ("chatgpt.com", "xn--e1afmkfd.xn--90ae"), or null if it
    /// is not a usable domain. A leading "www." is dropped because both forms are blocked.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string s = input.Trim();

        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        int cut = s.IndexOfAny(new[] { '/', '?', '#' });
        if (cut >= 0) s = s[..cut];
        int at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        int colon = s.IndexOf(':');
        if (colon >= 0) s = s[..colon];
        s = s.TrimEnd('.');
        if (s.StartsWith("*.", StringComparison.Ordinal)) s = s[2..];

        try
        {
            s = Idn.GetAscii(s).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (s.StartsWith("www.", StringComparison.Ordinal) && s.Count(c => c == '.') >= 2) s = s[4..];
        return s.Length <= 253 && HostRegex().IsMatch(s) ? s : null;
    }

    // At least two labels, the last one alphabetic (or punycode), so IP addresses are rejected.
    [GeneratedRegex(@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{1,59})$")]
    private static partial Regex HostRegex();
}
