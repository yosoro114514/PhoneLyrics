using System.Windows.Automation;

namespace PhoneLyrics;

// Single background MTA worker, woken by shell structure changes or foreground events.
// No media reads and no periodic UIA polling. Debounce shell animations before measuring.
internal sealed class TaskbarLayoutMonitor : IDisposable
{
    private readonly AutoResetEvent wake = new(false);
    private readonly TraceLog log;
    private readonly int monitorIndex;
    private readonly TaskbarNative.WinEventCallback callback;
    private readonly IntPtr foregroundHook;
    private volatile bool disposed;
    public event Action<TaskbarLayout?, string?>? Updated;
    public TaskbarLayoutMonitor(TraceLog log, int monitorIndex)
    {
        this.log = log; this.monitorIndex = monitorIndex;
        callback = (_, _, _, _, _, _, _) => Refresh();
        foregroundHook = TaskbarNative.SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2);
        var worker = new Thread(Work) { IsBackground = true, Name = "Taskbar geometry (MTA)" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    public void Refresh() { if (!disposed) { try { wake.Set(); } catch (ObjectDisposedException) { } } }
    private void Work()
    {
        AutomationElement? subscribed = null;
        var handler = new StructureChangedEventHandler((_, _) => Refresh());
        void Unsubscribe()
        {
            if (subscribed == null) return;
            try { Automation.RemoveStructureChangedEventHandler(subscribed, handler); } catch { }
            subscribed = null;
        }
        try
        {
            while (!disposed)
            {
                wake.WaitOne(); if (disposed) break;
                Thread.Sleep(350); // background-only shell layout debounce, not phone polling
                while (wake.WaitOne(0)) { }
                if (disposed) break;
                try
                {
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    var layout = TaskbarLayoutProbe.Read(monitorIndex);
                    if (subscribed == null || subscribed.Current.NativeWindowHandle != unchecked((int)layout.Handle.ToInt64()))
                    {
                        Unsubscribe(); subscribed = AutomationElement.FromHandle(layout.Handle);
                        Automation.AddStructureChangedEventHandler(subscribed, TreeScope.Subtree, handler);
                    }
                    log.Write("taskbarLayoutMeasured", new { triggeredByEvent = true, periodicUiaQueries = 0, namesRead = false,
                        durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds });
                    if (!disposed) Updated?.Invoke(layout, null);
                }
                catch (Exception ex)
                {
                    Unsubscribe();
                    log.Write("taskbarLayoutFailed", new { error = ex.Message });
                    if (!disposed) Updated?.Invoke(null, ex.Message);
                }
            }
        }
        finally { Unsubscribe(); wake.Dispose(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { wake.Set(); } catch (ObjectDisposedException) { }
        if (foregroundHook != IntPtr.Zero) TaskbarNative.UnhookWinEvent(foregroundHook);
        // Do not join a worker inside a synchronous Explorer UIA call.
    }
}
