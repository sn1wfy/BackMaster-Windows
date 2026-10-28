using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace BackMaster.Platform;

public static class Elevation
{
    private const int ErrorCancelled = 1223;

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Runs the current executable with administrator rights (a UAC prompt) and waits.
    /// Returns its exit code, or null if the user declined the prompt.
    /// </summary>
    public static int? RunSelfElevated(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(start)!;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
    }
}
