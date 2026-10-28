namespace BackMaster.Core.Hotkeys;

/// <summary>Values match the Win32 MOD_* flags passed to RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// A global shortcut such as "Ctrl+Alt+Shift+B". Kept to letters, digits and F1-F24 so the
/// text form round-trips and the virtual-key code is known without Windows Forms.
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>The Win32 virtual-key code for <see cref="Key"/>.</summary>
    public int VirtualKey => Key.Length == 1 ? Key[0] : 0x70 + int.Parse(Key[1..]) - 1;

    public static HotkeyGesture Parse(string text) =>
        TryParse(text, out var g, out var error) ? g : throw new FormatException(error);

    public static bool TryParse(string? text, out HotkeyGesture gesture, out string error)
    {
        gesture = null!;
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "the shortcut is empty.";
            return false;
        }

        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var mod = raw.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
            if (mod != HotkeyModifiers.None)
            {
                mods |= mod;
                continue;
            }
            if (key is not null)
            {
                error = "use exactly one key besides the modifiers.";
                return false;
            }
            key = raw.ToUpperInvariant();
        }

        if (key is null || !IsSupportedKey(key))
        {
            error = "the key must be a letter A-Z, a digit 0-9 or F1-F24.";
            return false;
        }
        if ((mods & (HotkeyModifiers.Control | HotkeyModifiers.Alt)) == 0)
        {
            error = "include Ctrl or Alt so normal typing cannot trigger it.";
            return false;
        }
        gesture = new HotkeyGesture(mods, key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    private static bool IsSupportedKey(string key)
    {
        if (key.Length == 1) return key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9';
        return key[0] == 'F' && int.TryParse(key[1..], out int n) && n is >= 1 and <= 24 && key[1] != '0';
    }
}
