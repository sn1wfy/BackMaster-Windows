using System.Text;

namespace BackMaster.Core.Filtering;

/// <summary>
/// Maintains BackMaster's block of entries inside the Windows hosts file, between two
/// marker lines, leaving everything else untouched. The hosts file has no wildcards, so
/// each domain is listed bare and with "www.". Both IPv4 and IPv6 are sinkholed so a
/// browser cannot fall back to the AAAA record.
/// </summary>
public static class HostsSection
{
    public const string BeginMarker = "# BEGIN BackMaster (managed automatically - do not edit)";
    public const string EndMarker = "# END BackMaster";

    public static IEnumerable<string> HostNames(IEnumerable<string> domains)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in domains)
        {
            if (seen.Add(d)) yield return d;
            if (!d.StartsWith("www.", StringComparison.Ordinal) && seen.Add("www." + d)) yield return "www." + d;
        }
    }

    /// <summary>
    /// Returns <paramref name="existing"/> with BackMaster's section replaced by one blocking
    /// <paramref name="domains"/>, or removed entirely when the list is empty. Applying the
    /// same list twice gives the same text.
    /// </summary>
    public static string Apply(string existing, IReadOnlyCollection<string> domains)
    {
        if (domains.Count == 0 && !HasSection(existing)) return existing;
        // Windows line endings unless the file clearly uses Unix ones.
        string newline = existing.Contains('\n') && !existing.Contains("\r\n", StringComparison.Ordinal) ? "\n" : "\r\n";
        var kept = Strip(existing, newline);

        if (domains.Count == 0) return kept;

        var sb = new StringBuilder(kept);
        sb.Append(BeginMarker).Append(newline);
        foreach (var host in HostNames(domains))
        {
            sb.Append("0.0.0.0 ").Append(host).Append(newline);
            sb.Append(":: ").Append(host).Append(newline);
        }
        sb.Append(EndMarker).Append(newline);
        return sb.ToString();
    }

    public static bool HasSection(string text) => text.Contains(BeginMarker, StringComparison.Ordinal);

    /// <summary>
    /// Removes every BackMaster section; an unterminated one is removed to the end of the
    /// file. The result is empty or ends with a newline, ready for a section to be appended.
    /// </summary>
    private static string Strip(string text, string newline)
    {
        if (text.Length == 0) return "";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (text.EndsWith('\n')) lines.RemoveAt(lines.Count - 1);

        var output = new List<string>(lines.Count);
        bool inside = false;
        foreach (var line in lines)
        {
            if (!inside && line.Trim() == BeginMarker) inside = true;
            else if (inside && line.Trim() == EndMarker) inside = false;
            else if (!inside) output.Add(line);
        }
        return output.Count == 0 ? "" : string.Join(newline, output) + newline;
    }
}
