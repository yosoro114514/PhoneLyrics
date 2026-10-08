using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PhoneLyrics;

internal sealed record PixelInterval(int Start, int End);
internal sealed record TaskbarLayout(IntPtr Handle, int Left, int Top, int Width, int Height, uint Dpi, PixelInterval[] Occupied);

internal static class TaskbarGeometry
{
    public static PixelInterval? ChooseGap(int width, IEnumerable<PixelInterval> occupied, int desired, int minimum)
    {
        var edge = 12;
        var gaps = new List<PixelInterval>();
        var start = edge;
        foreach (var item in occupied.OrderBy(x => x.Start))
        {
            var left = Math.Clamp(item.Start - edge, 0, width);
            var right = Math.Clamp(item.End + edge, 0, width);
            if (left > start) gaps.Add(new(start, left));
            start = Math.Max(start, right);
        }
        if (width - edge > start) gaps.Add(new(start, width - edge));
        var best = gaps.Where(g => g.End - g.Start >= minimum).OrderByDescending(g => g.End - g.Start).FirstOrDefault();
        if (best == null) return null;
        var size = Math.Min(desired, best.End - best.Start);
        var offset = best.Start + (best.End - best.Start - size) / 2;
        return new(offset, offset + size);
    }
}

// Only taskbar control types and rectangles are read. No app titles, notification text or UIA Names.
// Call on a background MTA thread; never synchronously query Explorer from a child window's UI thread.
internal static class TaskbarLayoutProbe
{
    public static TaskbarLayout Read(int monitorIndex = 0)
    {
        var bars = TaskbarNative.Taskbars();
        var tray = bars.ElementAtOrDefault(monitorIndex);
        if (tray == IntPtr.Zero && bars.Length > 0) tray = bars[0];
        if (tray == IntPtr.Zero || !TaskbarNative.GetWindowRect(tray, out var rect) || !TaskbarNative.GetClientRect(tray, out var client))
            throw new InvalidOperationException("找不到主屏任务栏。");
        if (client.Width < client.Height || client.Height < 20) throw new InvalidOperationException("当前仅支持横向任务栏。");
        var occupied = new List<PixelInterval>();
        var walker = TreeWalker.RawViewWalker;
        var root = AutomationElement.FromHandle(tray);
        var xaml = TaskbarNative.FindWindowEx(tray, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null) != IntPtr.Zero;
        var count = 0;
        void Visit(AutomationElement parent, int depth)
        {
            if (depth > 12 || count > 512) return;
            for (var element = walker.GetFirstChild(parent); element != null; element = walker.GetNextSibling(element))
            {
                if (++count > 512) return;
                try
                {
                    var properties = element.Current;
                    if (properties.ProcessId == Environment.ProcessId) continue;
                    var handle = new IntPtr(properties.NativeWindowHandle);
                    if (xaml && handle != IntPtr.Zero && TaskbarNative.ClassName(handle) is "ReBarWindow32" or "WorkerW") continue;
                    var bounds = properties.BoundingRectangle;
                    if (properties.IsOffscreen || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;
                    var type = properties.ControlType;
                    if (bounds.Width < client.Width * .94 && (type == ControlType.Button || type == ControlType.Text ||
                        type == ControlType.Image || type == ControlType.MenuItem || type == ControlType.ListItem))
                    {
                        var left = Math.Clamp((int)Math.Floor(bounds.Left - rect.Left), 0, client.Width);
                        var right = Math.Clamp((int)Math.Ceiling(bounds.Right - rect.Left), 0, client.Width);
                        if (right > left) occupied.Add(new(left, right));
                    }
                    else Visit(element, depth + 1);
                }
                catch (ElementNotAvailableException) { }
            }
        }
        Visit(root, 0);
        var notify = TaskbarNative.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && TaskbarNative.GetWindowRect(notify, out var notifyRect) && notifyRect.Width > 0)
            occupied.Add(new(Math.Clamp(notifyRect.Left - rect.Left, 0, client.Width), Math.Clamp(notifyRect.Right - rect.Left, 0, client.Width)));
        if (occupied.Count == 0) throw new InvalidOperationException("未取得任务栏图标范围，暂不嵌入歌词。");
        return new(tray, rect.Left, rect.Top, client.Width, client.Height, TaskbarNative.GetDpiForWindow(tray), occupied.ToArray());
    }
}

internal static class TaskbarNative
{
    [StructLayout(LayoutKind.Sequential)] private struct MouseTracking { public uint Size, Flags; public IntPtr Handle; public uint HoverTime; }
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref MouseTracking tracking);
    internal static void TrackLeave(IntPtr hwnd)
    { var tracking = new MouseTracking { Size = (uint)Marshal.SizeOf<MouseTracking>(), Flags = 2, Handle = hwnd }; TrackMouseEvent(ref tracking); }
    internal static IntPtr[] Taskbars()
    {
        var bars = new List<IntPtr>();
        foreach (var name in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            var after = IntPtr.Zero;
            while ((after = FindWindowEx(IntPtr.Zero, after, name, null)) != IntPtr.Zero) bars.Add(after);
        }
        return bars.ToArray();
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    internal delegate void WinEventCallback(IntPtr hook, uint type, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int count);
    internal static string ClassName(IntPtr hwnd) { var name = new System.Text.StringBuilder(128); GetClassName(hwnd, name, name.Capacity); return name.ToString(); }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetStyle(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern IntPtr SetStyle(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr hwnd, IntPtr parent);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint from, uint to, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
    internal static void Attach(IntPtr hwnd, IntPtr parent)
    {
        if (GetParent(hwnd) == parent) return;
        var oldStyle = GetStyle(hwnd, -16);
        SetStyle(hwnd, -16, new IntPtr((oldStyle.ToInt64() & ~0x80000000L) | 0x40000000L));
        Marshal.SetLastPInvokeError(0);
        var previous = SetParent(hwnd, parent);
        var error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0) { SetStyle(hwnd, -16, oldStyle); throw new System.ComponentModel.Win32Exception(error); }
        if (GetParent(hwnd) != parent) throw new InvalidOperationException("歌词窗口未能挂入任务栏。");
        var ex = GetStyle(hwnd, -20).ToInt64();
        SetStyle(hwnd, -20, new IntPtr((ex | 0x80L | 0x08000000L) & ~0x8L));
        SendMessage(hwnd, 0x02E3, IntPtr.Zero, IntPtr.Zero); // WPF refresh after parent DPI changes.
    }
}
