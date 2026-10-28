using BackMaster.Core.Companion;
using BackMaster.Core.Hotkeys;
using BackMaster.Core.Ipc;
using BackMaster.Core.Scheduling;
using BackMaster.Platform;

namespace BackMaster.Tray;

/// <summary>
/// The student-facing tray icon. It shows what the service is doing, and the fox card
/// announces lessons and explains blocked sites. It enforces nothing itself, so closing
/// it changes nothing.
/// The configured hotkey opens the phrase dialog, and the right phrase opens settings.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    // Short, so the fox appears while the student is still looking at the failed page.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

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
    private bool _polling;
    private long? _attemptCursor;
    private FoxPopup? _fox;
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
        if (_polling) return;
        _polling = true;
        try
        {
            await PollOnceAsync();
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task PollOnceAsync()
    {
        RefreshHotkey();
        var response = await _client.StatusAsync(_attemptCursor);
        _status = response?.Status;

        // The first reply only sets the cursor, so attempts from before logon are not replayed.
        if (response?.Cursor is long cursor)
        {
            if (_attemptCursor is not null && response.Attempts is { Count: > 0 } attempts)
                ShowFox(FoxCard.Blocked(_messages.Blocked(attempts[^1]), attempts[^1].IsAi, _messages.RandomTip(_random)));
            _attemptCursor = cursor;
        }

        string text = _messages.TrayText(_status);
        _statusItem.Text = text;
        _icon.Text = text.Length <= 127 ? text : text[..127];
        _icon.Icon = _status is null ? TrayIcons.Unknown : _status.Enforce ? TrayIcons.Lesson : TrayIcons.Free;

        if (_status is not null)
        {
            // Announce transitions, but not the state found at logon.
            if (_lastEnforce is not null && _lastEnforce != _status.Enforce && _status.Mode == EnforcementMode.Scheduled)
            {
                ShowFox(_status.Enforce
                    ? FoxCard.Lesson("ЧАС", _messages.Starting(_status), _messages.RandomTip(_random))
                    : FoxCard.Lesson("БРАВО", _messages.LessonEnding));
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

    private void ShowTip()
    {
        if (_messages.Tips.Count == 0) return;
        string tip = _messages.Tips[_random.Next(_messages.Tips.Count)];
        ShowFox(new FoxCard(FoxTone.Tip, "СЪВЕТ", "Съвет от лисицата", char.ToUpper(tip[0]) + tip[1..]));
    }

    /// <summary>Shows a fox card, replacing any card still on screen.</summary>
    private void ShowFox(FoxCard card)
    {
        _fox?.Close();
        _fox = new FoxPopup(card, _messages.Dismiss);
        var shown = _fox;
        shown.FormClosed += (_, _) =>
        {
            if (_fox == shown) _fox = null;
            shown.Dispose();
        };
        shown.ShowInCorner();
    }

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
            _fox?.Close();
            _icon.Visible = false;
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
