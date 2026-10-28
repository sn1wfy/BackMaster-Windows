using BackMaster.Core.Ipc;
using BackMaster.Platform;

namespace BackMaster.Tray;

/// <summary>
/// Asks for the unlock phrase and has the service check it. The tray never sees the
/// verifier; a wrong phrase leaves the settings closed and counts toward the lockout.
/// </summary>
internal sealed class PhraseDialog : Form
{
    private readonly PipeClient _client;
    private readonly TextBox _phrase;
    private readonly Label _message;
    private readonly Button _ok;

    public bool Unlocked { get; private set; }

    public PhraseDialog(PipeClient client)
    {
        _client = client;

        Text = "BackMaster";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = "Въведете фразата за достъп:", AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
        _phrase = new TextBox { UseSystemPasswordChar = true, Width = 320, MaxLength = 256 };
        layout.Controls.Add(_phrase);
        _message = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(320, 0), Margin = new Padding(0, 6, 0, 6) };
        layout.Controls.Add(_message);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        var cancel = new Button { Text = "Отказ", DialogResult = DialogResult.Cancel, AutoSize = true };
        _ok = new Button { Text = "Отвори", AutoSize = true };
        _ok.Click += async (_, _) => await TryUnlockAsync();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_ok);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        AcceptButton = _ok;
        CancelButton = cancel;
    }

    private async Task TryUnlockAsync()
    {
        string phrase = _phrase.Text;
        if (phrase.Length == 0) return;

        _ok.Enabled = _phrase.Enabled = false;
        _message.ForeColor = SystemColors.GrayText;
        _message.Text = "Проверка…";
        var response = await _client.UnlockAsync(phrase);
        _phrase.Clear();
        _ok.Enabled = _phrase.Enabled = true;
        _message.ForeColor = Color.Firebrick;

        switch (response?.Unlock)
        {
            case UnlockResult.Accepted:
                Unlocked = true;
                Close();
                return;
            case UnlockResult.Rejected:
                _message.Text = "Грешна фраза.";
                break;
            case UnlockResult.LockedOut:
                _message.Text = $"Твърде много грешни опити. Опитайте отново след {response.RetryAfterSeconds} сек.";
                break;
            case UnlockResult.NotConfigured:
                _message.Text = "Не е зададена фраза. Администраторът трябва да отвори „BackMaster Settings (Administrator)“ от менюто Старт и да зададе фраза.";
                break;
            default:
                _message.Text = "Службата на BackMaster не отговаря. Администраторът може да отвори „BackMaster Settings (Administrator)“ от менюто Старт.";
                break;
        }
        _phrase.Focus();
    }
}
