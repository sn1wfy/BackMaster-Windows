using System.Runtime.InteropServices;
using BackMaster.Core.Hotkeys;

namespace BackMaster.Tray;

/// <summary>A hidden window that owns one system-wide hotkey registration.</summary>
internal sealed partial class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int Id = 0xB4C;
    private const uint ModNoRepeat = 0x4000;

    private bool _registered;

    public event Action? Pressed;

    public HotkeyGesture? Current { get; private set; }

    public HotkeyWindow() => CreateHandle(new CreateParams());

    /// <returns>False if another program already uses the combination.</returns>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        _registered = RegisterHotKey(Handle, Id, (uint)gesture.Modifiers | ModNoRepeat, (uint)gesture.VirtualKey);
        Current = _registered ? gesture : null;
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) UnregisterHotKey(Handle, Id);
        _registered = false;
        Current = null;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && (int)m.WParam == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}
