using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using BackMaster.Core.Companion;

namespace BackMaster.Tray;

/// <summary>Chip colour of a card, by what it is about.</summary>
internal enum FoxTone { Ai, Site, App, Lesson, Tip }

/// <summary>What the fox says in one card. Hint and tip may be empty.</summary>
internal sealed record FoxCard(FoxTone Tone, string Chip, string Title, string Body, string Hint = "", string Tip = "")
{
    public static FoxCard Blocked(FoxMessage m, bool isAi, string tip) =>
        new(isAi ? FoxTone.Ai : FoxTone.Site, m.Chip, m.Title, m.Body, m.Hint, tip);

    public static FoxCard Lesson(string chip, TitledMessage m, string tip = "") => new(FoxTone.Lesson, chip, m.Title, m.Body, "", tip);
}

/// <summary>A rendered card and the areas that close it, in bitmap pixels.</summary>
internal sealed record RenderedCard(Bitmap Bitmap, Rectangle CloseButton, Rectangle DismissButton) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// Draws the fox card as a transparent bitmap: a dark rounded panel with a blue rule, the
/// fox standing at its left with its ears above the top edge and its tail fading out
/// behind a speech bubble, the BACKMASTER wordmark with a coloured chip, and a Разбрах
/// button. Coordinates follow the design mock-up (popup_mock.py) in logical pixels.
/// </summary>
internal static class FoxCardRenderer
{
    // Panel size from the mock-up; the height grows if the text needs it.
    private const float PanelWidth = 720, MinPanelHeight = 304;
    // Room around the panel for the shadow and for the fox's ears above it.
    private const float MarginLeft = 24, MarginTop = 44, MarginRight = 24, MarginBottom = 32;
    private const float Radius = 16;
    private const float FoxHeight = 338, FoxLeft = 14, FadeStart = 205, FadeEnd = 262;
    private const float TextColumn = 268;

    private static readonly Color BubbleColor = Color.FromArgb(246, 248, 252);
    private static readonly Color Ink = Color.FromArgb(46, 48, 53);

    public static Color ChipColor(FoxTone tone) => tone switch
    {
        FoxTone.Ai => Color.FromArgb(240, 90, 152, 241),
        FoxTone.App => Color.FromArgb(240, 242, 121, 29),
        FoxTone.Site => Color.FromArgb(240, 226, 84, 110),
        FoxTone.Lesson => Color.FromArgb(240, 46, 160, 98),
        _ => Color.FromArgb(240, 32, 160, 170),
    };

    public static RenderedCard Render(FoxCard card, string dismissText, float scale)
    {
        float s = scale;
        using var title = Font(15, bold: true, s);
        using var body = Font(10.5f, bold: false, s);
        using var hint = Font(10, bold: true, s);
        using var tipFont = Font(9, bold: false, s);
        using var brand = Font(9, bold: true, s);
        using var chip = Font(8, bold: true, s);
        using var button = Font(10, bold: true, s);
        var typo = StringFormat.GenericTypographic;

        // Measure the bubble first: it decides the panel height.
        using var measure = Graphics.FromHwnd(IntPtr.Zero);
        float ox = MarginLeft * s, oy = MarginTop * s, w = PanelWidth * s;
        float bx = ox + TextColumn * s;
        float x0 = bx - 8 * s, y0 = oy + 50 * s, x1 = ox + w - 22 * s;
        float tx = x0 + 18 * s, tw = (x1 - x0) - 36 * s;
        var bodyLines = Wrap(measure, card.Body, body, tw, typo);
        var hintLines = card.Hint.Length > 0 ? Wrap(measure, card.Hint, hint, tw, typo) : new List<string>();
        var tipLines = card.Tip.Length > 0 ? Wrap(measure, card.Tip, tipFont, tw, typo) : new List<string>();
        float bubbleHeight = 16 * s + 30 * s + bodyLines.Count * 18 * s
                             + (hintLines.Count > 0 ? 6 * s + hintLines.Count * 17 * s : 0)
                             + (tipLines.Count > 0 ? 14 * s + tipLines.Count * 15 * s : 0) + 14 * s;
        float h = Math.Max(MinPanelHeight * s, 50 * s + bubbleHeight + 12 * s + 34 * s + 22 * s);

        int width = (int)Math.Ceiling(ox + w + MarginRight * s);
        int height = (int)Math.Ceiling(oy + h + MarginBottom * s);
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var panel = new RectangleF(ox, oy, w, h);
        using var panelPath = Rounded(panel, Radius * s);

        DrawShadow(g, panel, s);

        using (var fill = new LinearGradientBrush(panel, Color.FromArgb(250, 69, 72, 79), Color.FromArgb(250, 46, 48, 53), LinearGradientMode.Vertical))
            g.FillPath(fill, panelPath);
        using (var outline = new Pen(Color.FromArgb(40, 255, 255, 255), s))
            g.DrawPath(outline, panelPath);

        // Blue rule down the left edge.
        var rule = new RectangleF(ox, oy + 16 * s, 7 * s, h - 32 * s);
        using (var ruleBrush = new LinearGradientBrush(rule, Color.FromArgb(74, 145, 242), Color.FromArgb(27, 87, 200), LinearGradientMode.Vertical))
            g.FillRectangle(ruleBrush, rule);

        DrawFox(g, panel, panelPath, s);

        // Header: wordmark, chip, close cross.
        const string Wordmark = "B A C K M A S T E R";
        using (var brandBrush = new SolidBrush(Color.FromArgb(158, 255, 255, 255)))
            g.DrawString(Wordmark, brand, brandBrush, bx, oy + 20 * s, typo);
        float cx = bx + Math.Max(150 * s, g.MeasureString(Wordmark, brand, PointF.Empty, typo).Width + 24 * s);
        float cw = g.MeasureString(card.Chip, chip, PointF.Empty, typo).Width;
        if (card.Chip.Length > 0)
        {
            var chipRect = new RectangleF(cx, oy + 17 * s, cw + 18 * s, 18 * s);
            using var chipPath = Rounded(chipRect, 9 * s);
            using var chipBrush = new SolidBrush(ChipColor(card.Tone));
            g.FillPath(chipBrush, chipPath);
            g.DrawString(card.Chip, chip, Brushes.White, cx + 9 * s, oy + 20.5f * s, typo);
        }
        var close = new RectangleF(ox + w - 40 * s, oy + 10 * s, 24 * s, 24 * s);
        using (var cross = new Pen(Color.FromArgb(170, 255, 255, 255), 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float c = 5 * s, mx = close.X + close.Width / 2, my = close.Y + close.Height / 2;
            g.DrawLine(cross, mx - c, my - c, mx + c, my + c);
            g.DrawLine(cross, mx - c, my + c, mx + c, my - c);
        }

        // Speech bubble, tail pointing at the fox.
        float y1 = y0 + bubbleHeight;
        using (var bubbleBrush = new SolidBrush(BubbleColor))
        {
            using var bubblePath = Rounded(new RectangleF(x0, y0, x1 - x0, y1 - y0), 16 * s);
            g.FillPath(bubbleBrush, bubblePath);
            g.FillPolygon(bubbleBrush, new[] { new PointF(x0 + 1 * s, y0 + 64 * s), new PointF(x0 - 20 * s, y0 + 82 * s), new PointF(x0 + 1 * s, y0 + 90 * s) });
        }

        float y = y0 + 16 * s;
        using (var titleBrush = new SolidBrush(Color.FromArgb(31, 95, 208)))
            g.DrawString(card.Title, title, titleBrush, tx, y, typo);
        y += 30 * s;
        using (var inkBrush = new SolidBrush(Ink))
        {
            foreach (var line in bodyLines) { g.DrawString(line, body, inkBrush, tx, y, typo); y += 18 * s; }
            if (hintLines.Count > 0)
            {
                y += 6 * s;
                foreach (var line in hintLines) { g.DrawString(line, hint, inkBrush, tx, y, typo); y += 17 * s; }
            }
        }
        if (tipLines.Count > 0)
        {
            y += 6 * s;
            using (var rulePen = new Pen(Color.FromArgb(212, 218, 228), s)) g.DrawLine(rulePen, tx, y, tx + tw, y);
            y += 8 * s;
            using var tipBrush = new SolidBrush(Color.FromArgb(98, 104, 116));
            foreach (var line in tipLines) { g.DrawString(line, tipFont, tipBrush, tx, y, typo); y += 15 * s; }
        }

        // Dismiss button, bottom right.
        float lw = g.MeasureString(dismissText, button, PointF.Empty, typo).Width;
        var dismiss = new RectangleF(ox + w - 22 * s - lw - 44 * s, oy + h - 56 * s, lw + 44 * s, 34 * s);
        using (var buttonPath = Rounded(dismiss, 17 * s))
        using (var buttonBrush = new SolidBrush(Color.FromArgb(47, 115, 226)))
            g.FillPath(buttonBrush, buttonPath);
        g.DrawString(dismissText, button, Brushes.White, dismiss.X + 22 * s, dismiss.Y + 8.5f * s, typo);

        return new RenderedCard(bmp, Rectangle.Round(close), Rectangle.Round(dismiss));
    }

    /// <summary>
    /// The fox stands on the panel's bottom edge and is clipped to it, except above the
    /// top edge where the ears stick out. Its right side fades so the tail disappears
    /// behind the bubble.
    /// </summary>
    private static void DrawFox(Graphics g, RectangleF panel, GraphicsPath panelPath, float s)
    {
        var art = FoxArt.Image;
        int fh = (int)(FoxHeight * s), fw = (int)(art.Width * (float)fh / art.Height);
        using var fox = new Bitmap(fw, fh, PixelFormat.Format32bppArgb);
        using (var fg = Graphics.FromImage(fox))
        {
            fg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            fg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            fg.DrawImage(art, 0, 0, fw, fh);
        }
        FadeRight(fox, (int)(FadeStart * s), (int)(FadeEnd * s));

        float fx = panel.X + FoxLeft * s, fy = panel.Bottom - fh;
        using var clip = new Region(new RectangleF(fx, fy, fw, panel.Y - fy));
        clip.Union(panelPath);
        var state = g.Save();
        g.SetClip(clip, CombineMode.Replace);
        g.DrawImage(fox, fx, fy, fw, fh);
        g.Restore(state);
    }

    private static void FadeRight(Bitmap bmp, int from, int to)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < bmp.Height; y++)
            {
                nint ptr = data.Scan0 + y * data.Stride;
                System.Runtime.InteropServices.Marshal.Copy(ptr, row, 0, row.Length);
                for (int x = Math.Max(0, from); x < bmp.Width; x++)
                {
                    float keep = x >= to ? 0 : 1 - (x - from) / (float)(to - from);
                    row[x * 4 + 3] = (byte)(row[x * 4 + 3] * keep);
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, ptr, row.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>A soft shadow below the panel, built from stacked translucent outlines.</summary>
    private static void DrawShadow(Graphics g, RectangleF panel, float s)
    {
        var baseRect = new RectangleF(panel.X + 4 * s, panel.Y + 10 * s, panel.Width - 4 * s, panel.Height - 2 * s);
        const int Steps = 12;
        for (int i = Steps; i >= 1; i--)
        {
            var r = RectangleF.Inflate(baseRect, i * 1.2f * s, i * 1.2f * s);
            using var path = Rounded(r, (18 + i) * s);
            using var brush = new SolidBrush(Color.FromArgb(9, 0, 0, 0));
            g.FillPath(brush, path);
        }
    }

    private static List<string> Wrap(Graphics g, string text, Font font, float maxWidth, StringFormat format)
    {
        var lines = new List<string>();
        string current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length == 0 || g.MeasureString(candidate, font, PointF.Empty, format).Width <= maxWidth) current = candidate;
            else { lines.Add(current); current = word; }
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static Font Font(float size, bool bold, float scale) =>
        new("Segoe UI", size * 1.08f * scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
