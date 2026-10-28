using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BackMaster.Tray;

/// <summary>Simple drawn icons until the fox artwork exists: orange = lesson, green = free, grey = no service.</summary>
internal static partial class TrayIcons
{
    public static readonly Icon Lesson = Draw(Color.FromArgb(0xE8, 0x6A, 0x17));
    public static readonly Icon Free = Draw(Color.FromArgb(0x2E, 0x9E, 0x5B));
    public static readonly Icon Unknown = Draw(Color.FromArgb(0x80, 0x80, 0x80));

    private static Icon Draw(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("B", font, Brushes.White, new RectangleF(0, 1, 32, 32), format);
        }
        nint handle = bmp.GetHicon();
        // Clone so the icon owns its data, then free the GDI handle.
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);
}
