using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PhoneLyrics;

// The media connection stays in the independent controller window; Explorer only hosts this view.
internal sealed class TaskbarHost : IDisposable
{
    private readonly TraceLog log;
    private readonly Dispatcher dispatcher;
    private readonly Func<OverlayPreferences> preferences;
    private readonly TaskbarLayoutMonitor monitor;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskbarLyricWindow? window;
    private AmsSnapshot? state;
    private string previousLine = "";
    private Brush fill = Brushes.Gold;
    private bool busy, disposed, locked;
    private string? lastPlacement;
    public event Action? FloatingRequested;
    public event Action<string>? ControlRequested;
    public event Action<FrameworkElement>? SettingsRequested;
    public event Action<int>? FontChangeRequested;
    public event Action<string>? Unavailable;
    public TaskbarHost(TraceLog log, Dispatcher dispatcher, Func<OverlayPreferences> preferences)
    {
        this.log = log; this.dispatcher = dispatcher; this.preferences = preferences;
        monitor = new(log, preferences().TaskbarMonitor);
        monitor.Updated += (layout, error) => dispatcher.BeginInvoke(() => { if (!disposed) Apply(layout, error); });
    }
    public async Task<bool> StartAsync()
    {
        monitor.Refresh();
        try { return await ready.Task.WaitAsync(TimeSpan.FromSeconds(6)) && window is { IsVisible: true }; }
        catch (TimeoutException) { log.Write("taskbarAttachFailed", new { reason = "LayoutTimeout" }); return false; }
    }
    public void Refresh() => monitor.Refresh();
    public void Reveal() => window?.Reveal();
    private void Apply(TaskbarLayout? layout, string? error)
    {
        try
        {
            if (layout == null) throw new InvalidOperationException(error ?? "任务栏暂不可用。");
            var scale = layout.Dpi / 96d;
            var gap = TaskbarGeometry.ChooseGap(layout.Width, layout.Occupied, (int)(preferences().TaskbarWidth * scale), (int)(220 * scale))
                ?? throw new InvalidOperationException("任务栏没有足够空位，已保留桌面浮窗。");
            if (window == null || !TaskbarNative.IsWindow(window.Handle))
            {
                var view = new TaskbarLyricWindow { Left = layout.Left / scale, Top = layout.Top / scale,
                    Width = (gap.End - gap.Start) / scale, Height = layout.Height / scale };
                view.FloatingRequested += () => FloatingRequested?.Invoke();
                view.ControlRequested += action => ControlRequested?.Invoke(action);
                view.SettingsRequested += target => SettingsRequested?.Invoke(target);
                view.FontChangeRequested += delta => FontChangeRequested?.Invoke(delta);
                view.Closed += (_, _) => { if (ReferenceEquals(window, view)) window = null; if (!disposed) Refresh(); };
                new WindowInteropHelper(view).EnsureHandle();
                window = view; view.Place(layout, gap); view.Show();
                view.SetLocked(locked);
            }
            window.Place(layout, gap); window.Update(state, previousLine, preferences(), fill, busy);
            var fingerprint = $"{layout.Handle}:{gap.Start}:{gap.End}:{layout.Height}:{layout.Dpi}";
            if (lastPlacement != fingerprint)
            {
                log.Write("taskbarAttached", new { hwnd = window.Handle.ToInt64(), parent = TaskbarNative.GetParent(window.Handle).ToInt64(),
                    expectedParent = layout.Handle.ToInt64(), gap, layout.Left, layout.Top, layout.Height, layout.Dpi,
                    occupied = layout.Occupied, geometryNamesRead = false, mediaSourceReads = 0 });
                lastPlacement = fingerprint;
            }
            ready.TrySetResult(true);
        }
        catch (Exception ex)
        {
            log.Write("taskbarAttachFailed", new { error = ex.Message });
            window?.Hide();
            if (!ready.TrySetResult(false)) Unavailable?.Invoke(ex.Message);
        }
    }
    public void Update(AmsSnapshot? state, string previousLine, Brush fill, bool busy)
    {
        this.state = state; this.previousLine = previousLine; this.fill = fill; this.busy = busy;
        window?.Update(state, previousLine, preferences(), fill, busy);
    }
    public void UpdatePosition(AmsSnapshot? state) => window?.UpdatePosition(state);
    public void SetLocked(bool value) { locked = value; window?.SetLocked(value); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; monitor.Dispose(); window?.Close(); window = null;
    }
}
