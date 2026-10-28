using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BackMaster.Tray;

/// <summary>The fox's head with a status dot: orange = lesson, green = free, grey = no service.</summary>
internal static partial class TrayIcons
{
    // The head (ears to chin) inside fox-companion.png, as fractions of the image size.
    private static readonly RectangleF Head = new(0.02f, 0.0f, 0.58f, 0.58f);

    public static readonly Icon Lesson = Draw(Color.FromArgb(0xE8, 0x6A, 0x17));
    public static readonly Icon Free = Draw(Color.FromArgb(0x2E, 0x9E, 0x5B));
    public static readonly Icon Unknown = Draw(Color.FromArgb(0x80, 0x80, 0x80));

    private static Icon Draw(Color status)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var art = FoxArt.Image;
            var source = new RectangleF(Head.X * art.Width, Head.Y * art.Height, Head.Width * art.Width, Head.Height * art.Width);
            g.DrawImage(art, new RectangleF(0, 0, 32, 32), source, GraphicsUnit.Pixel);

            using var ring = new SolidBrush(Color.White);
            using var dot = new SolidBrush(status);
            g.FillEllipse(ring, 19, 19, 13, 13);
            g.FillEllipse(dot, 21, 21, 9, 9);
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
