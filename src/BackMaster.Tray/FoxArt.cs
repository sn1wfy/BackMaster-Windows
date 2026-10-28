using System.Drawing.Drawing2D;

namespace BackMaster.Tray;

/// <summary>
/// The fox's face. Uses assets\companion\fox.png next to the executable when present, so
/// real artwork can be dropped in without rebuilding; otherwise draws a simple placeholder.
/// </summary>
internal static class FoxArt
{
    private static readonly Lazy<Image?> Artwork = new(LoadArtwork);

    public static string ArtworkPath => Path.Combine(AppContext.BaseDirectory, "assets", "companion", "fox.png");

    public static void Draw(Graphics g, RectangleF area)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (Artwork.Value is { } image)
        {
            float scale = Math.Min(area.Width / image.Width, area.Height / image.Height);
            var size = new SizeF(image.Width * scale, image.Height * scale);
            g.DrawImage(image, area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height);
            return;
        }
        DrawPlaceholder(g, area);
    }

    private static Image? LoadArtwork()
    {
        try
        {
            return File.Exists(ArtworkPath) ? Image.FromFile(ArtworkPath) : null;
        }
        catch (Exception e) when (e is OutOfMemoryException or IOException or ArgumentException)
        {
            return null; // not a usable image; fall back to the drawing
        }
    }

    /// <summary>A friendly fox face drawn on a 100x100 grid scaled into <paramref name="area"/>.</summary>
    private static void DrawPlaceholder(Graphics g, RectangleF area)
    {
        var state = g.Save();
        float s = Math.Min(area.Width, area.Height) / 100f;
        g.TranslateTransform(area.X + (area.Width - 100 * s) / 2, area.Y + (area.Height - 100 * s) / 2);
        g.ScaleTransform(s, s);

        using var orange = new SolidBrush(Color.FromArgb(0xE8, 0x70, 0x1A));
        using var darkOrange = new SolidBrush(Color.FromArgb(0xB8, 0x4E, 0x0C));
        using var cream = new SolidBrush(Color.FromArgb(0xFF, 0xF6, 0xEC));
        using var ink = new SolidBrush(Color.FromArgb(0x2B, 0x1D, 0x14));

        // Ears, with dark inner ears.
        g.FillPolygon(orange, new PointF[] { new(14, 6), new(8, 50), new(42, 30) });
        g.FillPolygon(orange, new PointF[] { new(86, 6), new(92, 50), new(58, 30) });
        g.FillPolygon(darkOrange, new PointF[] { new(17, 15), new(14, 42), new(34, 31) });
        g.FillPolygon(darkOrange, new PointF[] { new(83, 15), new(86, 42), new(66, 31) });

        // Head: wide at the cheeks, tapering to the muzzle.
        g.FillClosedCurve(orange, new PointF[] { new(50, 22), new(84, 32), new(96, 58), new(66, 80), new(50, 94), new(34, 80), new(4, 58), new(16, 32) }, FillMode.Alternate, 0.45f);

        // White cheeks and muzzle.
        g.FillClosedCurve(cream, new PointF[] { new(6, 58), new(30, 60), new(44, 70), new(50, 92), new(56, 70), new(70, 60), new(94, 58), new(68, 80), new(50, 95), new(32, 80) }, FillMode.Alternate, 0.35f);

        // Eyes with a highlight, and the nose.
        g.FillEllipse(ink, 31, 45, 9, 12);
        g.FillEllipse(ink, 60, 45, 9, 12);
        g.FillEllipse(Brushes.White, 34, 47, 3.5f, 3.5f);
        g.FillEllipse(Brushes.White, 63, 47, 3.5f, 3.5f);
        g.FillEllipse(ink, 44, 82, 12, 8);

        g.Restore(state);
    }
}
