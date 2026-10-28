using System.Runtime.InteropServices;
using BackMaster.Core.Companion;

namespace BackMaster.Tray;

/// <summary>What the fox says in one card. Chip and hint may be empty.</summary>
internal sealed record FoxCard(string Chip, string Title, string Body, string Hint = "", string Footer = "")
{
    public static FoxCard From(FoxMessage m, string footer = "") => new(m.Chip, m.Title, m.Body, m.Hint, footer);
    public static FoxCard From(TitledMessage m) => new("", m.Title, m.Body);
}

/// <summary>
/// The fox card in the bottom-right corner. It never takes focus from what the student is
/// typing, stays on top, and closes itself after a while unless the mouse is over it.
/// </summary>
internal sealed partial class FoxPopup : Form
{
    private static readonly Color Accent = Color.FromArgb(0xE8, 0x70, 0x1A);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);
    private const int CardWidth = 400;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private DateTime _hoveredUntil = DateTime.MinValue;

    public FoxPopup(FoxCard card, string dismissText)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 4, 0, 0); // room for the accent strip painted in OnPaint
        Opacity = 0;

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(14, 12, 16, 12), BackColor = Color.White };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var fox = new Panel { Size = new Size(88, 88), Margin = new Padding(0, 2, 12, 0) };
        fox.Paint += (_, e) => FoxArt.Draw(e.Graphics, new RectangleF(0, 0, fox.Width, fox.Height));
        layout.Controls.Add(fox, 0, 0);

        int textWidth = CardWidth - 100 - 30;
        var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        if (card.Chip.Length > 0)
            text.Controls.Add(new Label
            {
                Text = card.Chip, AutoSize = true, ForeColor = Color.White, BackColor = Accent,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Padding = new Padding(5, 2, 5, 2), Margin = new Padding(0, 0, 0, 6),
            });
        text.Controls.Add(Paragraph(card.Title, 12f, FontStyle.Bold, Color.FromArgb(0x2B, 0x1D, 0x14), textWidth));
        text.Controls.Add(Paragraph(card.Body, 9.75f, FontStyle.Regular, Color.FromArgb(0x33, 0x33, 0x33), textWidth));
        if (card.Hint.Length > 0) text.Controls.Add(Paragraph(card.Hint, 9f, FontStyle.Italic, Color.FromArgb(0x6B, 0x5B, 0x4E), textWidth));
        if (card.Footer.Length > 0) text.Controls.Add(Paragraph(card.Footer, 9f, FontStyle.Regular, Accent, textWidth));

        var dismiss = new Button
        {
            Text = dismissText, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), Padding = new Padding(10, 2, 10, 2), Margin = new Padding(0, 6, 0, 0),
            Cursor = Cursors.Hand,
        };
        dismiss.FlatAppearance.BorderSize = 0;
        dismiss.Click += (_, _) => Close();
        text.Controls.Add(dismiss);
        layout.Controls.Add(text, 1, 0);

        Controls.Add(layout);
        _timer.Tick += OnTick;
    }

    /// <summary>Shows the card in the bottom-right corner of the primary screen.</summary>
    public void ShowInCorner()
    {
        CreateControl();
        PerformLayout();
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
        Show();
        _timer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x20000, WsExToolWindow = 0x80, WsExTopmost = 0x8, WsExNoActivate = 0x08000000;
            var cp = base.CreateParams;
            cp.ClassStyle |= CsDropShadow;
            cp.ExStyle |= WsExToolWindow | WsExTopmost | WsExNoActivate;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Rounded corners on Windows 11; ignored on Windows 10.
        int round = 2;
        _ = DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var strip = new SolidBrush(Accent);
        e.Graphics.FillRectangle(strip, 0, 0, Width, 4);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (Opacity < 1) Opacity = Math.Min(1, Opacity + 0.15);
        if (Bounds.Contains(Cursor.Position)) _hoveredUntil = now.AddSeconds(3);
        if (now - _shownAt > Lifetime && now > _hoveredUntil) Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private static Label Paragraph(string text, float size, FontStyle style, Color color, int width) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        Font = new Font("Segoe UI", size, style),
        ForeColor = color,
        Margin = new Padding(0, 0, 0, 4),
    };

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
