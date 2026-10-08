using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace PhoneLyrics;

internal sealed class TaskbarLyricWindow : Window
{
    private readonly Grid root = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    private readonly Border hover = new() { Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), CornerRadius = new CornerRadius(5), Visibility = Visibility.Hidden };
    private readonly Grid text = new() { Margin = new Thickness(6, 2, 6, 3) };
    private readonly LyricText lyric = new() { Compact = true, FontSize = 18 };
    private readonly LyricText previousLyric = new() { Compact = true, FontSize = 12, Opacity = .7 };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Height = 2, BorderThickness = new Thickness(0),
        Foreground = new SolidColorBrush(Color.FromRgb(255, 208, 108)), Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
        VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(9, 0, 9, 1), ToolTip = "手机播放进度（只读）" };
    private readonly StackPanel controls = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Button previous, playPause, next;
    private bool locked;
    private HwndSource? native;
    internal IntPtr Handle => new WindowInteropHelper(this).Handle;
    public event Action? FloatingRequested;
    public event Action<string>? ControlRequested;
    public event Action<FrameworkElement>? SettingsRequested;
    public event Action<int>? FontChangeRequested;

    public TaskbarLyricWindow()
    {
        Title = "手机任务栏歌词";
        Icon = AppIcon.CreateWindowIcon();
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = 480; Height = 48; UseLayoutRounding = true;
        SourceInitialized += (_, _) => { native = HwndSource.FromHwnd(Handle); native.AddHook(MouseHook); };
        Closed += (_, _) => { if (native is { IsDisposed: false }) native.RemoveHook(MouseHook); };
        root.Children.Add(hover);
        text.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        text.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        text.Children.Add(lyric); Grid.SetRow(previousLyric, 1); text.Children.Add(previousLyric);
        root.Children.Add(text); root.Children.Add(progress); root.Children.Add(controls); Content = root;
        AutomationProperties.SetAutomationId(lyric, "TaskbarCurrentLyricText");
        AutomationProperties.SetAutomationId(progress, "TaskbarPlaybackProgress");
        AutomationProperties.SetName(progress, "手机播放进度（只读）");
        var buttonStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="surface" Background="Transparent" CornerRadius="5">
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="#28FFFFFF"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Background" Value="#40FFD36A"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.35"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """);
        Button Make(string content, string name, string id)
        {
            var button = new Button { Content = content, Style = buttonStyle, Width = 27, Height = 28, Padding = new Thickness(0),
                FontSize = 13, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                ToolTip = name, Cursor = System.Windows.Input.Cursors.Hand, Focusable = false };
            AutomationProperties.SetName(button, name); AutomationProperties.SetAutomationId(button, id); controls.Children.Add(button); return button;
        }
        previous = Make("⏮", "上一首", "TaskbarPreviousButton");
        playPause = Make("▶", "播放 / 暂停", "TaskbarPlayPauseButton");
        next = Make("⏭", "下一首", "TaskbarNextButton");
        var floating = Make("↗", "切回桌面浮窗", "ReturnToFloatingButton");
        previous.Click += (_, _) => ControlRequested?.Invoke("previous");
        next.Click += (_, _) => ControlRequested?.Invoke("next");
        playPause.Click += (_, _) => ControlRequested?.Invoke(playPause.Tag as string ?? "pause");
        floating.Click += (_, _) => FloatingRequested?.Invoke();
        root.MouseEnter += (_, _) => SetHover(true);
        root.MouseLeave += (_, _) => SetHover(false);
        root.MouseRightButtonUp += (_, e) => { if (!locked) { SettingsRequested?.Invoke(root); e.Handled = true; } };
        root.MouseWheel += (_, e) => { if (!locked) { FontChangeRequested?.Invoke(e.Delta > 0 ? 1 : -1); e.Handled = true; } };
    }
    private void SetHover(bool visible)
    {
        visible &= !locked;
        hover.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        controls.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        text.Margin = new Thickness(visible ? 111 : 6, 2, 6, 3);
    }
    public void Reveal() => SetHover(true);
    private IntPtr MouseHook(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
    {
        // WPF's top-level hover bookkeeping can lag after cross-process SetParent.
        if (message == 0x0200) { SetHover(true); TaskbarNative.TrackLeave(hwnd); }
        if (message == 0x02A3) SetHover(false);
        return IntPtr.Zero;
    }
    public void Place(TaskbarLayout layout, PixelInterval gap)
    {
        var scale = layout.Dpi / 96d;
        Width = (gap.End - gap.Start) / scale; Height = layout.Height / scale;
        TaskbarNative.Attach(Handle, layout.Handle);
        if (!TaskbarNative.SetWindowPos(Handle, IntPtr.Zero, gap.Start, 0, gap.End - gap.Start, layout.Height, 0x0010 | 0x0020))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
    }
    public void SetLocked(bool value)
    {
        locked = value; var ex = TaskbarNative.GetStyle(Handle, -20).ToInt64();
        TaskbarNative.SetStyle(Handle, -20, new IntPtr(value ? ex | 0x20 : ex & ~0x20));
        SetHover(false);
    }
    public void Update(AmsSnapshot? state, string previousLine, OverlayPreferences preferences, Brush fill, bool busy)
    {
        lyric.Text = state?.PrimaryText is { Length: > 0 } current ? current : state is { Connected: true } ? "等待手机歌词…" : "手机尚未连接";
        previousLyric.Text = previousLine;
        lyric.FontSize = preferences.TaskbarFontSize; previousLyric.FontSize = Math.Max(11, preferences.TaskbarFontSize * .7);
        lyric.Fill = previousLyric.Fill = fill;
        lyric.Opacity = state is { Connected: true } ? preferences.TextOpacity : preferences.TextOpacity * .55;
        previousLyric.Visibility = preferences.TaskbarTwoLines ? Visibility.Visible : Visibility.Collapsed;
        text.RowDefinitions[1].Height = new GridLength(preferences.TaskbarTwoLines ? 16 : 0);
        playPause.Content = state?.Playback?.State == 0 ? "▶" : "❚❚";
        playPause.Tag = state?.Playback?.State == 0 ? "play" : "pause";
        var enabled = !busy && state is { Connected: true } && !string.IsNullOrWhiteSpace(state.Player);
        bool Supports(int command) => state != null && (state.SupportedCommands.Length == 0 || state.SupportedCommands.Contains(command));
        previous.IsEnabled = enabled && Supports(4); next.IsEnabled = enabled && Supports(3);
        playPause.IsEnabled = enabled && state?.Playback?.State is 0 or 1 && Supports(state.Playback.State == 0 ? 0 : 1);
        UpdatePosition(state);
    }
    public void UpdatePosition(AmsSnapshot? state)
    {
        var seconds = state?.PositionAt(AmsSession.MonotonicSeconds);
        var known = seconds != null && state?.Duration is > 0;
        progress.Visibility = known ? Visibility.Visible : Visibility.Hidden;
        progress.Value = known ? Math.Clamp(seconds!.Value / state!.Duration!.Value, 0, 1) : 0;
        var time = seconds is { } value ? $"{(int)value / 60}:{(int)value % 60:00}" : "—:—";
        root.ToolTip = $"{state?.Player} · {state?.Artist}\n{state?.PrimaryText}\n{time}（进度只读）\n右键设置 · 滚轮调字号 · 悬停显示控制";
    }
}
