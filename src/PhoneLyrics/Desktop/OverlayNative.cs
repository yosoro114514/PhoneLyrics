using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PhoneLyrics;

internal sealed class OverlayNative : IDisposable
{
    private const int ExStyle = -20, Transparent = 0x20, ToolWindow = 0x80, HotKeyId = 0x4C52;
    private readonly HwndSource source;
    private readonly uint taskbarCreated = TaskbarNative.RegisterWindowMessage("TaskbarCreated");
    public bool HotKeyRegistered { get; }
    public string? HotKeyText { get; }
    public bool ClickThrough { get; private set; }
    public event Action? ToggleRequested;
    public event Action? TaskbarRestarted;
    public OverlayNative(Window window, bool registerHotKey)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source.AddHook(Hook);
        SetWindowLongPtr(source.Handle, ExStyle, new IntPtr(GetWindowLongPtr(source.Handle, ExStyle).ToInt64() | ToolWindow));
        if (registerHotKey)
        {
            if (RegisterHotKey(source.Handle, HotKeyId, 0x4003, 0x4C))
            { HotKeyRegistered = true; HotKeyText = "Ctrl+Alt+L"; }
            else if (RegisterHotKey(source.Handle, HotKeyId, 0x4007, 0x4C))
            { HotKeyRegistered = true; HotKeyText = "Ctrl+Alt+Shift+L"; }
        }
    }
    public void SetClickThrough(bool enabled)
    {
        var style = GetWindowLongPtr(source.Handle, ExStyle).ToInt64();
        Marshal.SetLastPInvokeError(0);
        var result = SetWindowLongPtr(source.Handle, ExStyle, new IntPtr(enabled ? style | Transparent : style & ~Transparent));
        if (result == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        ClickThrough = enabled;
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HotKeyId) { handled = true; ToggleRequested?.Invoke(); }
        if ((uint)msg == taskbarCreated) TaskbarRestarted?.Invoke();
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (HotKeyRegistered) UnregisterHotKey(source.Handle, HotKeyId);
        source.RemoveHook(Hook);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
