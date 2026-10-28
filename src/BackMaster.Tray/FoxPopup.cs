using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BackMaster.Tray;

/// <summary>
/// The fox card in the bottom-right corner. It is a layered window drawn from a
/// transparent bitmap, so the fox's ears and the shadow can extend past the panel. It
/// never takes focus from what the student is typing, stays on top, and closes itself
/// after a while unless the mouse is over it.
/// </summary>
internal sealed partial class FoxPopup : Form
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(20);
    private const int FadeSteps = 8;

    private readonly RenderedCard _card;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private DateTime _hoveredUntil = DateTime.MinValue;
    private int _fade;

    public FoxPopup(FoxCard card, string dismissText)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        _card = FoxCardRenderer.Render(card, dismissText, CurrentScale());
        Size = _card.Bitmap.Size;
        _timer.Tick += OnTick;
    }

    /// <summary>Shows the card in the bottom-right corner of the primary screen.</summary>
    public void ShowInCorner()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Right - Width - 4, area.Bottom - Height - 4);
        Show();
        Present(0);
        _timer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExLayered = 0x80000, WsExToolWindow = 0x80, WsExTopmost = 0x8, WsExNoActivate = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExLayered | WsExToolWindow | WsExTopmost | WsExNoActivate;
            return cp;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = _card.CloseButton.Contains(e.Location) || _card.DismissButton.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && (_card.CloseButton.Contains(e.Location) || _card.DismissButton.Contains(e.Location)))
            Close();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (_fade < FadeSteps) Present(255 * ++_fade / FadeSteps);
        if (Bounds.Contains(Cursor.Position)) _hoveredUntil = now.AddSeconds(3);
        if (now - _shownAt > Lifetime && now > _hoveredUntil) Close();
    }

    /// <summary>Puts the card's pixels on screen with the given overall opacity (0-255).</summary>
    private void Present(int opacity)
    {
        nint screen = GetDC(0);
        nint memory = CreateCompatibleDC(screen);
        nint bitmap = _card.Bitmap.GetHbitmap(Color.FromArgb(0));
        nint previous = SelectObject(memory, bitmap);
        try
        {
            var size = new NativeSize(Width, Height);
            var source = new NativePoint(0, 0);
            var position = new NativePoint(Left, Top);
            var blend = new BlendFunction { SourceConstantAlpha = (byte)opacity, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref source, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    private static float CurrentScale()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        float scale = g.DpiX / 96f;
        // Keep the card within ~60% of the screen width on small classroom monitors.
        var area = Screen.PrimaryScreen?.WorkingArea.Width ?? 1280;
        return Math.Min(scale, area * 0.6f / 770f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _card.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x, Y = y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int cx, int cy) { public int Cx = cx, Cy = cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref NativePoint pptDst, ref NativeSize psize,
        nint hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

    [LibraryImport("user32.dll")] private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint hwnd, nint hdc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint hdc);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(nint hdc);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint hdc, nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint obj);
}
