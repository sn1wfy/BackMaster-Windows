using System.Globalization;
using BackMaster.Core.Companion;
using BackMaster.Core.Filtering;
using BackMaster.Core.Hotkeys;
using BackMaster.Core.Policy;
using BackMaster.Core.Scheduling;
using BackMaster.Core.Security;
using BackMaster.Platform;

namespace BackMaster.Tray;

/// <summary>
/// The administrator's settings. Opening it needs the phrase (or an elevated start from
/// the Start menu); saving always needs Windows administrator approval, because the files
/// it writes are protected. The window itself never changes the machine.
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly CultureInfo Bulgarian = CultureInfo.GetCultureInfo("bg-BG");

    private sealed record Choice<T>(T Value, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly DataPaths _paths;
    private readonly CompanionMessages _messages;
    private readonly ClassroomPolicy _original;

    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _timeZone = new() { Width = 260 };
    private readonly DataGridView _periods = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        RowHeadersWidth = 28,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };
    private readonly TextBox _aiDomains = MultilineBox();
    private readonly TextBox _otherDomains = MultilineBox();
    private readonly TextBox _hotkey = new() { ReadOnly = true, Width = 260, BackColor = SystemColors.Window };
    private readonly Label _hotkeyHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly TextBox _newPhrase = new() { UseSystemPasswordChar = true, Width = 260, MaxLength = 256 };
    private readonly TextBox _confirmPhrase = new() { UseSystemPasswordChar = true, Width = 260, MaxLength = 256 };
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _save = new() { Text = "Запази…", AutoSize = true };

    public SettingsForm(DataPaths paths, CompanionMessages messages)
    {
        _paths = paths;
        _messages = messages;
        string? loadError = null;
        try
        {
            _original = PolicyStore.LoadOrDefault(paths);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _original = ClassroomPolicy.Default;
            loadError = e.Message;
        }

        Text = "BackMaster — настройки" + (Elevation.IsElevated ? " (администратор)" : "");
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 520);
        MinimumSize = new Size(560, 460);
        TopMost = true;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(ScheduleTab());
        tabs.TabPages.Add(SitesTab());
        tabs.TabPages.Add(AccessTab());

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var cancel = new Button { Text = "Затвори", AutoSize = true, DialogResult = DialogResult.Cancel };
        cancel.Click += (_, _) => Close();
        _save.Click += async (_, _) => await SaveAsync();
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(_save, 1, 0);
        bottom.Controls.Add(cancel, 2, 0);

        Controls.Add(tabs);
        Controls.Add(bottom);
        CancelButton = cancel;

        Fill(_original);
        if (loadError is not null)
            MessageBox.Show(this, "policy.json не може да бъде прочетен; показани са стандартните настройки.\n\n" + loadError, "BackMaster", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Shown += async (_, _) => await ShowServiceStatusAsync();
    }

    private TabPage ScheduleTab()
    {
        _mode.Items.AddRange(new object[]
        {
            new Choice<EnforcementMode>(EnforcementMode.Scheduled, "По график (само по време на час)"),
            new Choice<EnforcementMode>(EnforcementMode.AlwaysOn, "Винаги включено"),
            new Choice<EnforcementMode>(EnforcementMode.AlwaysOff, "Изключено"),
        });

        var days = Enum.GetValues<DayOfWeek>()
            .Select(d => new Choice<DayOfWeek>(d, Bulgarian.DateTimeFormat.GetDayName(d)))
            .ToList();
        _periods.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Ден",
            DataSource = days,
            ValueMember = nameof(Choice<DayOfWeek>.Value),
            DisplayMember = nameof(Choice<DayOfWeek>.Name),
            ValueType = typeof(DayOfWeek),
            FillWeight = 50,
        });
        _periods.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Начало (ЧЧ:ММ)", FillWeight = 25 });
        _periods.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Край (ЧЧ:ММ)", FillWeight = 25 });
        _periods.DataError += (_, e) => e.ThrowException = false;

        var grid = Grid(3);
        grid.Controls.Add(Caption("Режим:"), 0, 0);
        grid.Controls.Add(_mode, 1, 0);
        grid.Controls.Add(Caption("Часова зона:"), 0, 1);
        grid.Controls.Add(_timeZone, 1, 1);
        grid.Controls.Add(Caption("Часове:"), 0, 2);
        grid.Controls.Add(_periods, 1, 2);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return Page("График", grid);
    }

    private TabPage SitesTab()
    {
        var grid = Grid(3);
        grid.Controls.Add(Hint("По един домейн на ред. Блокира се и www. вариантът; другите поддомейни се добавят отделно. Блокирането важи само по време на час."), 0, 0);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 0)!, 2);
        grid.Controls.Add(Caption("AI асистенти:"), 0, 1);
        grid.Controls.Add(_aiDomains, 1, 1);
        grid.Controls.Add(Caption("Други сайтове:"), 0, 2);
        grid.Controls.Add(_otherDomains, 1, 2);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        return Page("Сайтове", grid);
    }

    private TabPage AccessTab()
    {
        _hotkey.KeyDown += CaptureHotkey;
        _hotkey.PreviewKeyDown += (_, e) => e.IsInputKey = true;

        var grid = Grid(6);
        grid.Controls.Add(Caption("Клавишна комбинация:"), 0, 0);
        grid.Controls.Add(_hotkey, 1, 0);
        grid.Controls.Add(_hotkeyHint, 1, 1);
        grid.Controls.Add(Caption("Нова фраза:"), 0, 2);
        grid.Controls.Add(_newPhrase, 1, 2);
        grid.Controls.Add(Caption("Повторете фразата:"), 0, 3);
        grid.Controls.Add(_confirmPhrase, 1, 3);
        bool hasPhrase = !Elevation.IsElevated || PhraseVerifier.IsWellFormed(PolicyStore.ReadVerifier(_paths));
        grid.Controls.Add(Hint(hasPhrase
            ? "Оставете полетата празни, за да запазите сегашната фраза. Пази се само неин хеш (PBKDF2-SHA256), достъпен само за администратори."
            : "Все още няма зададена фраза — задайте я тук, за да работи клавишната комбинация."), 1, 4);
        return Page("Достъп", grid);
    }

    private void Fill(ClassroomPolicy p)
    {
        _mode.SelectedItem = _mode.Items.Cast<Choice<EnforcementMode>>().First(c => c.Value == p.Schedule.Mode);
        _timeZone.Text = p.Schedule.TimeZone;
        _periods.Rows.Clear();
        foreach (var period in p.Schedule.Periods) _periods.Rows.Add(period.Day, period.Start, period.End);
        _aiDomains.Lines = p.Filtering.AiDomains.ToArray();
        _otherDomains.Lines = p.Filtering.BlockedDomains.ToArray();
        _hotkey.Text = p.Access.Hotkey;
        _hotkeyHint.Text = "Щракнете в полето и натиснете новата комбинация (Ctrl или Alt + буква, цифра или F1–F24).";
    }

    private void CaptureHotkey(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        string? key = e.KeyCode switch
        {
            >= Keys.A and <= Keys.Z => e.KeyCode.ToString(),
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (e.KeyCode - Keys.D0))).ToString(),
            >= Keys.F1 and <= Keys.F24 => e.KeyCode.ToString(),
            _ => null,
        };
        if (key is null) return; // a lone modifier; wait for the real key

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        parts.Add(key);
        string text = string.Join('+', parts);
        if (HotkeyGesture.TryParse(text, out var g, out var error))
        {
            _hotkey.Text = g.ToString();
            _hotkeyHint.ForeColor = SystemColors.GrayText;
            _hotkeyHint.Text = "Новата комбинация влиза в сила след запис.";
        }
        else
        {
            _hotkeyHint.ForeColor = Color.Firebrick;
            _hotkeyHint.Text = $"{text}: изберете комбинация с Ctrl или Alt.";
        }
    }

    internal ClassroomPolicy ReadForm(List<string> errors)
    {
        var periods = new List<PeriodSpec>();
        foreach (DataGridViewRow row in _periods.Rows)
        {
            if (row.IsNewRow) continue;
            if (row.Cells[0].Value is not DayOfWeek day)
            {
                errors.Add($"Ред {row.Index + 1}: изберете ден.");
                continue;
            }
            periods.Add(new PeriodSpec(day, row.Cells[1].Value?.ToString()?.Trim() ?? "", row.Cells[2].Value?.ToString()?.Trim() ?? ""));
        }

        return _original with
        {
            Schedule = new ScheduleSection(((Choice<EnforcementMode>)_mode.SelectedItem!).Value, _timeZone.Text.Trim(), periods),
            Filtering = new FilteringSection(Domains(_aiDomains), Domains(_otherDomains)),
            Access = new AccessSection(_hotkey.Text),
        };
    }

    /// <summary>Normalizes each line; unusable lines are kept as typed so validation names them.</summary>
    private static List<string> Domains(TextBox box) =>
        box.Lines.Select(l => l.Trim()).Where(l => l.Length > 0)
            .Select(l => DomainName.Normalize(l) ?? l)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private async Task SaveAsync()
    {
        var errors = new List<string>();
        var policy = ReadForm(errors);
        errors.AddRange(PolicyLoader.Errors(policy));

        string phrase = _newPhrase.Text;
        if (phrase.Length > 0 || _confirmPhrase.Text.Length > 0)
        {
            if (phrase != _confirmPhrase.Text) errors.Add("Двете полета за фраза не съвпадат.");
            else if (PhraseVerifier.Weakness(phrase) is { } why) errors.Add(why);
        }
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors), "Проверете настройките", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        UseWaitCursor = true;
        _save.Enabled = false;
        _status.Text = "Записване… потвърдете администраторското разрешение.";
        string? failure;
        try
        {
            string? verifier = phrase.Length > 0 ? await Task.Run(() => PhraseVerifier.Create(phrase)) : null;
            // Waiting for the elevated helper blocks; keep the window responsive.
            failure = await Task.Run(() => ApplyCommand.Save(new PolicyChange(policy, verifier)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            failure = e.Message;
        }
        finally
        {
            UseWaitCursor = false;
            _save.Enabled = true;
        }

        if (failure is null)
        {
            _newPhrase.Clear();
            _confirmPhrase.Clear();
            _status.Text = "Записано. Службата прилага промените до няколко секунди.";
            MessageBox.Show(this, "Настройките са записани.", "BackMaster", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            _status.Text = failure;
        }
    }

    private async Task ShowServiceStatusAsync()
    {
        var response = await new PipeClient().StatusAsync();
        var s = response?.Status;
        _status.Text = s is null
            ? _messages.TrayUnreachable
            : _messages.TrayText(s) + $" · блокирани домейни: {s.BlockedDomainCount}" + (s.PolicyError is null ? "" : " · ГРЕШКА в policy.json");
    }

    private static TextBox MultilineBox() => new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        AcceptsReturn = true,
        Dock = DockStyle.Fill,
        WordWrap = false,
    };

    private static TableLayoutPanel Grid(int rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows, Padding = new Padding(8) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 7, 8, 3) };

    private static Label Hint(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 6) };

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }
}
