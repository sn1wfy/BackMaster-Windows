using BackMaster.Core.Companion;
using BackMaster.Core.Hotkeys;
using BackMaster.Core.Ipc;
using BackMaster.Core.Scheduling;
using BackMaster.Platform;

namespace BackMaster.Tray;

/// <summary>
/// The student-facing tray icon. It shows what the service is doing and announces the
/// start and end of lessons; it enforces nothing itself, so closing it changes nothing.
/// The configured hotkey opens the phrase dialog, and the right phrase opens settings.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly CompanionMessages _messages = Messages.Load();
    private readonly PipeClient _client = new();
    private readonly DataPaths _paths = DataPaths.Default;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly HotkeyWindow _hotkey = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Random _random = new();

    private ServiceStatus? _status;
    private bool? _lastEnforce;
    private DateTime _policyStamp = DateTime.MinValue;
    private bool _hotkeyWarned;
    private PhraseDialog? _phraseDialog;
    private SettingsForm? _settings;

    public TrayApp()
    {
        _statusItem = new ToolStripMenuItem(_messages.TrayUnreachable) { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Съвет от лисицата", null, (_, _) => ShowTip());

        _icon = new NotifyIcon
        {
            Icon = TrayIcons.Unknown,
            Text = "BackMaster",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowTip();

        _hotkey.Pressed += OpenPhraseDialog;
        RefreshHotkey();

        _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        RefreshHotkey();
        var response = await _client.StatusAsync();
        _status = response?.Status;

        string text = _messages.TrayText(_status);
        _statusItem.Text = text;
        _icon.Text = text.Length <= 127 ? text : text[..127];
        _icon.Icon = _status is null ? TrayIcons.Unknown : _status.Enforce ? TrayIcons.Lesson : TrayIcons.Free;

        if (_status is not null)
        {
            // Announce transitions, but not the state found at logon.
            if (_lastEnforce is not null && _lastEnforce != _status.Enforce && _status.Mode == EnforcementMode.Scheduled)
            {
                var m = _status.Enforce ? _messages.Starting(_status) : _messages.LessonEnding;
                _icon.ShowBalloonTip(8000, m.Title, m.Body, ToolTipIcon.Info);
            }
            _lastEnforce = _status.Enforce;
        }
    }

    /// <summary>Picks up a changed hotkey from policy.json without restarting the tray.</summary>
    private void RefreshHotkey()
    {
        DateTime stamp;
        try
        {
            stamp = File.Exists(_paths.PolicyFile) ? File.GetLastWriteTimeUtc(_paths.PolicyFile) : DateTime.MinValue;
        }
        catch (IOException)
        {
            return;
        }
        if (stamp == _policyStamp && _hotkey.Current is not null) return;
        _policyStamp = stamp;

        HotkeyGesture gesture;
        try
        {
            gesture = HotkeyGesture.Parse(PolicyStore.LoadOrDefault(_paths).Access.Hotkey);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or FormatException)
        {
            gesture = HotkeyGesture.Parse(Core.Policy.AccessSection.Default.Hotkey);
        }
        if (gesture == _hotkey.Current) return;

        if (!_hotkey.Register(gesture) && !_hotkeyWarned)
        {
            _hotkeyWarned = true;
            _icon?.ShowBalloonTip(8000, "BackMaster",
                $"Клавишната комбинация {gesture} се използва от друга програма. Отворете настройките от менюто Старт и изберете друга.",
                ToolTipIcon.Warning);
        }
    }

    private void ShowTip() => _icon.ShowBalloonTip(6000, "BackMaster", _messages.RandomTip(_random), ToolTipIcon.None);

    private void OpenPhraseDialog()
    {
        if (_settings is { IsDisposed: false })
        {
            _settings.Activate();
            return;
        }
        if (_phraseDialog is { IsDisposed: false })
        {
            _phraseDialog.Activate();
            return;
        }

        _phraseDialog = new PhraseDialog(_client);
        _phraseDialog.FormClosed += (_, _) =>
        {
            bool unlocked = _phraseDialog.Unlocked;
            _phraseDialog.Dispose();
            _phraseDialog = null;
            if (unlocked) OpenSettings();
        };
        _phraseDialog.Show();
        _phraseDialog.Activate();
    }

    private void OpenSettings()
    {
        _settings = new SettingsForm(_paths, _messages);
        _settings.FormClosed += (_, _) =>
        {
            _settings.Dispose();
            _settings = null;
            _policyStamp = DateTime.MinValue; // re-read the hotkey in case it changed
            RefreshHotkey();
        };
        _settings.Show();
        _settings.Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _hotkey.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
