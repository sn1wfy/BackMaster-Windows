using System.Reflection;

namespace BackMaster.Tray;

/// <summary>
/// The fox companion artwork (transparent PNG). The copy embedded in the executable is
/// used unless assets\companion\fox-companion.png exists next to it, so the art can be
/// replaced on a PC without rebuilding.
/// </summary>
internal static class FoxArt
{
    private static readonly Lazy<Bitmap> Fox = new(Load);

    public static string OverridePath => Path.Combine(AppContext.BaseDirectory, "assets", "companion", "fox-companion.png");

    public static Bitmap Image => Fox.Value;

    private static Bitmap Load()
    {
        try
        {
            if (File.Exists(OverridePath)) return new Bitmap(OverridePath);
        }
        catch (Exception e) when (e is ArgumentException or IOException or OutOfMemoryException)
        {
            // Not a usable image; use the embedded one.
        }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("fox-companion.png")
                           ?? throw new InvalidOperationException("fox-companion.png is not embedded.");
        return new Bitmap(stream);
    }
}
