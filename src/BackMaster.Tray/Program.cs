using BackMaster.Platform;

namespace BackMaster.Tray;

internal static class Program
{
    /// <summary>
    /// BackMaster.Tray.exe                 tray icon for the signed-in user (started at logon)
    /// BackMaster.Tray.exe --admin         settings window for an administrator (Start menu shortcut)
    /// BackMaster.Tray.exe --apply F HASH  elevated helper: write the settings in F (internal)
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args is ["--apply", var file, var hash])
            return ApplyCommand.Run(file, hash);

        if (args is ["--admin"])
        {
            if (!Elevation.IsElevated)
                return Elevation.RunSelfElevated(new[] { "--admin" }) ?? 1;
            Application.Run(new SettingsForm(DataPaths.Default, Messages.Load()));
            return 0;
        }

        using var single = new Mutex(true, @"Local\BackMaster.Tray", out bool first);
        if (!first) return 0;
        Application.Run(new TrayApp());
        return 0;
    }
}
