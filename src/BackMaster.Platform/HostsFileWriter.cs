using System.Runtime.InteropServices;
using System.Text;
using BackMaster.Core.Filtering;

namespace BackMaster.Platform;

/// <summary>Applies the blocked-domain list to the real hosts file and flushes the DNS cache.</summary>
public sealed partial class HostsFileWriter
{
    // Latin-1 maps every byte to one char and back, so comments in any code page survive
    // a read-modify-write untouched. BackMaster's own entries are plain ASCII.
    private static readonly Encoding Bytes = Encoding.Latin1;

    public string Path { get; }

    public HostsFileWriter(string path) => Path = path;

    /// <returns>True if the file was changed.</returns>
    public bool Apply(IReadOnlyCollection<string> domains)
    {
        string existing = File.Exists(Path) ? File.ReadAllText(Path, Bytes) : "";
        string updated = HostsSection.Apply(existing, domains);
        if (updated == existing) return false;

        var info = new FileInfo(Path);
        if (info.Exists && info.IsReadOnly) info.IsReadOnly = false;
        WriteWithRetry(updated);
        FlushDnsCache();
        return true;
    }

    public bool IsBlocking() => File.Exists(Path) && HostsSection.HasSection(File.ReadAllText(Path, Bytes));

    /// <summary>Antivirus and the DNS client briefly lock the hosts file; retry a few times.</summary>
    private void WriteWithRetry(string text)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(Path, text, Bytes);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }

    /// <summary>Clears cached answers so a newly blocked site stops resolving immediately.</summary>
    public static void FlushDnsCache()
    {
        try
        {
            DnsFlushResolverCache();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not fatal: cached entries expire within minutes on their own.
        }
    }

    [LibraryImport("dnsapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsFlushResolverCache();
}
