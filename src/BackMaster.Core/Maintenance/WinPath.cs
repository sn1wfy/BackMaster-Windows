namespace BackMaster.Core.Maintenance;

/// <summary>
/// Windows path helpers that behave the same on every OS, so checks can be tested
/// off Windows (System.IO.Path follows the host's separator rules).
/// </summary>
public static class WinPath
{
    public static string Join(string dir, string relative) =>
        dir.Replace('/', '\\').TrimEnd('\\') + "\\" + relative.Replace('/', '\\').TrimStart('\\');

    /// <summary>
    /// The executable in a service command line such as
    /// <c>"C:\Program Files\BackMaster\BackMaster.Service.exe" --service</c>.
    /// Unquoted paths are cut after ".exe" because spaces are ambiguous without quotes.
    /// </summary>
    public static string? ExecutableFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var s = commandLine.Trim();
        if (s.StartsWith('"'))
        {
            int close = s.IndexOf('"', 1);
            return close > 1 ? s[1..close] : null;
        }
        int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? s[..(exe + 4)] : s.Split(' ')[0];
    }
}
