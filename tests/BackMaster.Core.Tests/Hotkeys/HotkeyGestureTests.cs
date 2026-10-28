using BackMaster.Core.Hotkeys;
using Xunit;

namespace BackMaster.Core.Tests.Hotkeys;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+B", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "B", 0x42)]
    [InlineData("control + alt + f12", HotkeyModifiers.Control | HotkeyModifiers.Alt, "F12", 0x7B)]
    [InlineData("Alt+Win+7", HotkeyModifiers.Alt | HotkeyModifiers.Win, "7", 0x37)]
    [InlineData("Shift+Ctrl+F1", HotkeyModifiers.Control | HotkeyModifiers.Shift, "F1", 0x70)]
    [InlineData("Ctrl+F24", HotkeyModifiers.Control, "F24", 0x87)]
    public void Parses_modifiers_key_and_virtual_key(string text, HotkeyModifiers mods, string key, int vk)
    {
        var g = HotkeyGesture.Parse(text);
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(key, g.Key);
        Assert.Equal(vk, g.VirtualKey);
    }

    [Theory]
    [InlineData("shift+alt+ctrl+b", "Ctrl+Alt+Shift+B")]
    [InlineData("Win+Ctrl+f9", "Ctrl+Win+F9")]
    public void Formats_in_canonical_order(string text, string expected)
    {
        var g = HotkeyGesture.Parse(text);
        Assert.Equal(expected, g.ToString());
        Assert.Equal(g, HotkeyGesture.Parse(g.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("B")]
    [InlineData("Shift+B")]          // would fire while typing capitals
    [InlineData("Win+B")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Space")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F01")]
    public void Rejects_unsafe_or_unsupported_shortcuts(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.NotEmpty(error);
    }
}
