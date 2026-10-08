using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PhoneLyrics;

internal sealed class PhoneOverlay : Window
{
    private readonly TraceLog log = new(null);
    private readonly SemaphoreSlim backendGate = new(1, 1);
    private readonly bool demo, savePreview, registerHotKey;
    private OverlayPreferences preferences = OverlayPreferences.Read();
    private AmsSession? ams;
    private AmsSnapshot? last;
    private string? boundDevice;
    private bool busy, closing, locked;
    private string previousLine = "";
    private string trackContext = "";
    private TaskbarHost? taskbarHost;
    private bool switchingView;
    private readonly Grid root = new() { Background = Brushes.Transparent, Margin = new Thickness(12) };
    private readonly Border toolbar = new() { CornerRadius = new CornerRadius(12), Height = 48,
        Background = new SolidColorBrush(Color.FromArgb(238, 24, 27, 34)), BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
        BorderThickness = new Thickness(1), Padding = new Thickness(8, 3, 8, 3), Visibility = Visibility.Hidden };
    private readonly LyricText lyric = new() { Height = 90 };
    private readonly LyricText previousLyric = new() { Height = 44, Opacity = .65 };
    private readonly TextBlock source = new() { Foreground = new SolidColorBrush(Color.FromRgb(218, 219, 224)), FontSize = 11,
        Text = "正在连接手机…", TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock position = new() { Foreground = new SolidColorBrush(Color.FromRgb(158, 163, 177)), FontSize = 11, Text = "—:—" };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Height = 4,
        Foreground = new SolidColorBrush(Color.FromRgb(255, 208, 108)), Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
        BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0),
        ToolTip = "手机播放进度（只读）。当前连接无法拖动到指定时间。", Cursor = Cursors.Arrow };
    private readonly Button previous, playPause, next, lockButton, themeButton, modeButton, displayButton;
    private readonly Style buttonStyle;
    private readonly DispatcherTimer renderClock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer hideToolbar = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private OverlayNative? native;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;

    public PhoneOverlay(bool savePreview = false, string? expectedDevice = null, bool demo = false, bool registerHotKey = true, string? displayMode = null)
    {
        this.savePreview = savePreview; this.demo = demo; this.registerHotKey = registerHotKey;
        if (displayMode != null)
        {
            if (displayMode is not "floating" and not "taskbar") throw new ArgumentException("--mode requires floating or taskbar");
            preferences = preferences with { DisplayMode = displayMode };
        }
        boundDevice = expectedDevice;
        log.Write("environment", EnvironmentInfo.Read());
        log.Write("overlayStarted", new { pid = Environment.ProcessId, backend = demo ? "VisualDemoNoPhone" : "AMS", directory = log.DirectoryPath,
            periodicSourceReads = 0, transparentLyrics = true, theme = preferences.Theme,
            settingsFile = OverlayPreferences.FilePath, executable = Environment.ProcessPath });
        Title = demo ? "手机桌面歌词 · 演示" : "手机桌面歌词";
        Icon = AppIcon.CreateWindowIcon();
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
        Width = preferences.Width; Height = preferences.TwoLines ? 210 : 168;
        buttonStyle = (Style)XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Setter Property="Foreground" Value="#E7E8EC"/><Setter Property="Background" Value="Transparent"/>
              <Setter Property="BorderThickness" Value="0"/><Setter Property="FontFamily" Value="Segoe UI, Microsoft YaHei UI"/>
              <Setter Property="FontSize" Value="12"/><Setter Property="Cursor" Value="Hand"/>
              <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="surface" Background="{TemplateBinding Background}" CornerRadius="7" Padding="{TemplateBinding Padding}">
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Background" Value="#24FFFFFF"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Background" Value="#40FFD36A"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.35"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """);
        previous = Button("⏮", "上一首", "PreviousButton", 34);
        playPause = Button("▶", "播放", "PlayPauseButton", 38);
        next = Button("⏭", "下一首", "NextButton", 34);
        previous.FontSize = next.FontSize = 17; playPause.FontSize = 19;
        lockButton = Button("锁定", "锁定歌词（鼠标穿透）", "LockLyricsButton", 48);
        themeButton = Button("暖金", "切换歌词配色", "ColorThemeButton", 48);
        modeButton = Button("双行", "单行或双行歌词", "DisplayModeButton", 48);
        displayButton = Button("任务栏", "切换为任务栏歌词", "SwitchToTaskbarButton", 58);

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var transport = new StackPanel { Orientation = Orientation.Horizontal };
        transport.Children.Add(previous); transport.Children.Add(playPause); transport.Children.Add(next);
        bar.Children.Add(transport);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), Cursor = Cursors.SizeAll };
        var timeline = new Grid();
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeline.Children.Add(position); Grid.SetColumn(progress, 1); timeline.Children.Add(progress);
        AutomationProperties.SetAutomationId(progress, "PhonePlaybackProgress");
        AutomationProperties.SetName(progress, "手机播放进度（只读，无法拖动）");
        info.Children.Add(source); info.Children.Add(timeline); Grid.SetColumn(info, 1); bar.Children.Add(info);
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        var smaller = Button("A−", "缩小字号", "FontSizeDownButton", 34);
        var larger = Button("A+", "放大字号", "FontSizeUpButton", 34);
        var more = Button("···", "更多设置", "MoreSettingsButton", 34);
        var close = Button("×", "退出桌面歌词", "OverlayCloseButton", 32); close.FontSize = 21;
        foreach (var button in new[] { smaller, larger, themeButton, modeButton, displayButton, lockButton, more, close }) tools.Children.Add(button);
        Grid.SetColumn(tools, 2); bar.Children.Add(tools); toolbar.Child = bar;
        root.Children.Add(toolbar);
        Grid.SetRow(lyric, 1); root.Children.Add(lyric);
        Grid.SetRow(previousLyric, 2); root.Children.Add(previousLyric);
        AutomationProperties.SetAutomationId(lyric, "CurrentLyricText");
        AutomationProperties.SetAutomationId(previousLyric, "PreviousLyricText");
        Content = root;

        previous.Click += async (_, _) => await SendAsync("previous");
        playPause.Click += async (_, _) => await SendAsync(last?.Playback?.State == 0 ? "play" : "pause");
        next.Click += async (_, _) => await SendAsync("next");
        smaller.Click += (_, _) => ChangeFont(-2); larger.Click += (_, _) => ChangeFont(2);
        modeButton.Click += (_, _) => { preferences = preferences with { TwoLines = !preferences.TwoLines }; ApplyAppearance(); SavePreferences(); };
        displayButton.Click += async (_, _) => await SetDisplayModeAsync("taskbar");
        themeButton.Click += (_, _) => ShowThemeMenu();
        lockButton.Click += (_, _) => SetLocked(!locked);
        more.Click += (_, _) => ShowSettingsMenu(more);
        close.Click += (_, _) => Close();
        source.MouseLeftButtonDown += Drag;
        lyric.MouseLeftButtonDown += Drag; previousLyric.MouseLeftButtonDown += Drag;
        lyric.MouseWheel += (_, e) => { if (!locked) { ChangeFont(e.Delta > 0 ? 2 : -2); e.Handled = true; } };
        root.MouseEnter += (_, _) => ShowToolbar();
        root.MouseLeave += (_, _) => { hideToolbar.Stop(); hideToolbar.Start(); };
        root.MouseRightButtonUp += (_, e) => { if (!locked) { ShowSettingsMenu(lyric); e.Handled = true; } };
        hideToolbar.Tick += (_, _) => { hideToolbar.Stop(); if (!root.IsMouseOver) HideToolbar(); };
        renderClock.Tick += (_, _) => RenderPosition();
        SourceInitialized += (_, _) =>
        {
            native = new OverlayNative(this, registerHotKey && !demo);
            native.ToggleRequested += () => SetLocked(!locked);
            native.TaskbarRestarted += () => taskbarHost?.Refresh();
            CreateTray();
            lockButton.ToolTip = native.HotKeyRegistered ? $"锁定歌词（鼠标穿透） · {native.HotKeyText} 解锁" : "锁定歌词（鼠标穿透） · 从托盘解锁";
            log.Write("overlayNativeReady", new { native.HotKeyRegistered, hotKey = native.HotKeyText, clickThrough = false });
        };
        Loaded += async (_, _) =>
        {
            PlaceWindow(); ApplyAppearance(); ShowToolbar();
            hideToolbar.Start(); renderClock.Start();
            if (demo)
            {
                ShowState(new("演示 iPhone", true, "视觉演示", "把喜欢的旋律，留在桌面上", "透明桌面歌词", "", 240, null,
                    new(0, 0, 72), AmsSession.MonotonicSeconds, DateTimeOffset.UtcNow, [], null));
                previousLine = "让每一句，都有自己的光"; previousLyric.Text = previousLine;
            }
            else await ConnectAsync();
            if (!closing && preferences.DisplayMode == "taskbar") await SetDisplayModeAsync("taskbar");
            if (savePreview && !closing) await SavePreviewAsync();
        };
        Closing += (_, _) =>
        {
            closing = true; renderClock.Stop(); hideToolbar.Stop(); SavePreferences();
            taskbarHost?.Dispose(); taskbarHost = null;
            native?.Dispose(); tray?.Dispose(); trayIcon?.Dispose(); SetControls(false);
        };
        Closed += async (_, _) =>
        {
            await backendGate.WaitAsync();
            try { if (ams != null) { ams.Changed -= OnState; await ams.DisposeAsync(); } }
            finally { backendGate.Release(); log.Write("overlayClosed", new { pid = Environment.ProcessId }); log.Dispose(); Application.Current.Shutdown(); }
        };
    }

    private Button Button(string content, string name, string id, double width)
    {
        var button = new Button { Content = content, Style = buttonStyle, Width = width, Height = 34, Padding = new Thickness(4), ToolTip = name };
        AutomationProperties.SetName(button, name); AutomationProperties.SetAutomationId(button, id); return button;
    }
    private void PlaceWindow(bool reset = false)
    {
        var work = SystemParameters.WorkArea;
        Width = Math.Min(preferences.Width, work.Width);
        var virtualLeft = SystemParameters.VirtualScreenLeft; var virtualTop = SystemParameters.VirtualScreenTop;
        Left = reset || preferences.Left == null ? work.Left + (work.Width - Width) / 2 : Math.Clamp(preferences.Left.Value, virtualLeft,
            Math.Max(virtualLeft, virtualLeft + SystemParameters.VirtualScreenWidth - Width));
        Top = reset || preferences.Top == null ? work.Bottom - Height - 36 : Math.Clamp(preferences.Top.Value, virtualTop,
            Math.Max(virtualTop, virtualTop + SystemParameters.VirtualScreenHeight - Height));
    }
    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (locked || e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); SavePreferences(); } catch (InvalidOperationException) { }
    }
    private void ShowToolbar()
    {
        if (locked || closing) return;
        hideToolbar.Stop(); toolbar.Visibility = Visibility.Visible;
        root.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // hover hit region, visually transparent
    }
    private void HideToolbar() { toolbar.Visibility = Visibility.Hidden; root.Background = Brushes.Transparent; }
    private void SetLocked(bool value)
    {
        if (native == null || closing) return;
        try
        {
            native.SetClickThrough(value); locked = value;
            taskbarHost?.SetLocked(value);
            if (locked) HideToolbar(); else { ShowToolbar(); hideToolbar.Start(); }
            lockButton.Content = locked ? "解锁" : "锁定";
            log.Write("overlayLockChanged", new { locked, hotKeyRegistered = native.HotKeyRegistered });
        }
        catch (Exception ex) { source.Text = "锁定失败，可从托盘重试"; log.Write("overlayLockFailed", new { error = ex.Message }); }
    }
    private void ApplyAppearance()
    {
        preferences = preferences.Normalize();
        var colors = preferences.Theme switch
        {
            "mint" => new[] { "#EEFFF4", "#73F4C4", "#25BFA0" },
            "white" => new[] { "#FFFFFF", "#F6F6F8", "#E2E4EA" },
            _ => new[] { "#FFF8DB", "#FFD06C", "#DFA137" }
        };
        var brush = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
        for (var i = 0; i < colors.Length; i++) brush.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(colors[i]), i / 2d));
        brush.Freeze(); lyric.Fill = previousLyric.Fill = brush;
        lyric.FontSize = preferences.FontSize; lyric.Opacity = preferences.TextOpacity;
        previousLyric.FontSize = Math.Max(22, preferences.FontSize * .65); previousLyric.Opacity = preferences.TextOpacity * .65;
        previousLyric.Visibility = preferences.TwoLines ? Visibility.Visible : Visibility.Collapsed;
        Height = preferences.TwoLines ? 210 : 168;
        Width = Math.Min(preferences.Width, SystemParameters.WorkArea.Width);
        if (IsLoaded) Top = Math.Clamp(Top, SystemParameters.VirtualScreenTop,
            Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
        themeButton.Content = preferences.Theme switch { "mint" => "青绿", "white" => "白色", _ => "暖金" };
        modeButton.Content = preferences.TwoLines ? "单行" : "双行";
        taskbarHost?.Update(last, previousLine, lyric.Fill, busy || demo);
    }
    private void ChangeFont(double delta) { preferences = preferences with { FontSize = preferences.FontSize + delta }; ApplyAppearance(); SavePreferences(); }
    private void SavePreferences()
    {
        if (demo) return;
        try
        {
            if (taskbarHost == null) preferences = preferences with { Left = Left, Top = Top, Width = Width };
            preferences.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Write("overlaySettingsSaveFailed", new { error = ex.Message }); }
    }
    private void ShowThemeMenu()
    {
        var menu = new ContextMenu();
        foreach (var (name, key) in new[] { ("暖金渐变", "gold"), ("青绿渐变", "mint"), ("白色", "white") })
            AddMenu(menu, name, () => { preferences = preferences with { Theme = key }; ApplyAppearance(); SavePreferences(); }, preferences.Theme == key);
        menu.PlacementTarget = themeButton; menu.IsOpen = true;
    }
    private void ShowSettingsMenu(FrameworkElement target)
    {
        var menu = new ContextMenu();
        AddMenu(menu, "桌面浮窗", () => _ = SetDisplayModeAsync("floating"), taskbarHost == null);
        AddMenu(menu, "任务栏歌词", () => _ = SetDisplayModeAsync("taskbar"), taskbarHost != null);
        if (taskbarHost != null)
        {
            AddMenu(menu, preferences.TaskbarTwoLines ? "任务栏改为单行" : "任务栏改为双行", () =>
            { preferences = preferences with { TaskbarTwoLines = !preferences.TaskbarTwoLines }; ApplyAppearance(); SavePreferences(); });
            var bars = TaskbarNative.Taskbars();
            for (var index = 0; index < bars.Length; index++)
            {
                var selectedIndex = index;
                AddMenu(menu, index == 0 ? "任务栏：主屏" : $"任务栏：屏幕 {index + 1}", () => _ = ChangeTaskbarMonitorAsync(selectedIndex), Math.Min(preferences.TaskbarMonitor, bars.Length - 1) == index);
            }
        }
        menu.Items.Add(new Separator());
        AddMenu(menu, "重新连接手机", () => _ = ConnectAsync());
        AddMenu(menu, "打开手机链接", () => Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\Microsoft.YourPhone_8wekyb3d8bbwe!App")
            { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }));
        menu.Items.Add(new Separator());
        AddMenu(menu, "宽度增加", () => { preferences = preferences with { Width = preferences.Width + 120 }; ApplyAppearance(); PlaceWindow(); SavePreferences(); });
        AddMenu(menu, "宽度减少", () => { preferences = preferences with { Width = preferences.Width - 120 }; ApplyAppearance(); PlaceWindow(); SavePreferences(); });
        foreach (var opacity in new[] { 1d, .85, .65 })
            AddMenu(menu, $"文字透明度 {opacity:P0}", () => { preferences = preferences with { TextOpacity = opacity }; ApplyAppearance(); SavePreferences(); }, preferences.TextOpacity == opacity);
        AddMenu(menu, "恢复浮窗到屏幕下方", () => _ = RestoreFloatingPositionAsync());
        menu.Items.Add(new Separator()); AddMenu(menu, native?.HotKeyText is { } shortcut ? $"锁定 / 解锁    {shortcut}" : "锁定 / 解锁（托盘可恢复）", () => SetLocked(!locked));
        AddMenu(menu, "退出", Close); menu.PlacementTarget = target; menu.IsOpen = true;
    }
    private static void AddMenu(ContextMenu menu, string text, Action action, bool selected = false)
    {
        var item = new MenuItem { Header = text, IsCheckable = selected, IsChecked = selected };
        item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    private void CreateTray()
    {
        trayIcon = AppIcon.CreateTrayIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = demo ? "手机桌面歌词（演示）" : "手机桌面歌词", Visible = true };
        log.Write("applicationIconLoaded", new { embedded = true, trayIcon.Width, trayIcon.Height });
        var menu = new Forms.ContextMenuStrip();
        void Item(string text, Action action) => menu.Items.Add(text, null, (_, _) => Dispatcher.BeginInvoke(action));
        var floating = menu.Items.Add("桌面浮窗", null, (_, _) => Dispatcher.BeginInvoke(() => _ = SetDisplayModeAsync("floating")));
        var taskbar = menu.Items.Add("任务栏歌词", null, (_, _) => Dispatcher.BeginInvoke(() => _ = SetDisplayModeAsync("taskbar")));
        menu.Opening += (_, _) => { ((Forms.ToolStripMenuItem)floating).Checked = taskbarHost == null; ((Forms.ToolStripMenuItem)taskbar).Checked = taskbarHost != null; };
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("编辑歌词 / 解锁", () => { SetLocked(false); if (taskbarHost == null) { Show(); WindowState = WindowState.Normal; ShowToolbar(); } });
        Item("锁定歌词（鼠标穿透）", () => SetLocked(true));
        Item("重新连接手机", () => _ = ConnectAsync());
        Item("恢复浮窗到屏幕下方", () => _ = RestoreFloatingPositionAsync());
        Item("打开设置与日志目录", () => Process.Start(new ProcessStartInfo(AppStorage.Root) { UseShellExecute = true }));
        Item("关于手机歌词 / 参考项目", ShowAbout);
        menu.Items.Add(new Forms.ToolStripSeparator()); Item("退出", Close);
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(async () => { await SetDisplayModeAsync("floating"); SetLocked(false); ShowToolbar(); });
    }

    public void Reveal()
    {
        if (closing) return;
        SetLocked(false);
        if (taskbarHost != null) { taskbarHost.Refresh(); taskbarHost.Reveal(); }
        else { Show(); WindowState = WindowState.Normal; ShowToolbar(); Activate(); }
        log.Write("existingInstanceActivated", new { mode = taskbarHost == null ? "floating" : "taskbar" });
    }

    private void ShowAbout()
    {
        var content = new TextBox
        {
            Text = $"PhoneLyrics {typeof(PhoneOverlay).Assembly.GetName().Version?.ToString(3)}\n桌面浮窗 / 任务栏歌词 · AMS 蓝牙通知\n进度条只读。\n\n设置与日志：" + AppStorage.Root + "\n\n" + AppIcon.Notices(),
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(18), BorderThickness = new Thickness(0)
        };
        new Window { Title = "关于手机歌词", Icon = Icon, Width = 620, Height = 480,
            Content = content, WindowStartupLocation = WindowStartupLocation.CenterScreen }.ShowDialog();
    }

    private async Task SetDisplayModeAsync(string mode)
    {
        if (closing || switchingView) return;
        if (mode == "floating")
        {
            var old = taskbarHost; taskbarHost = null; old?.Dispose();
            preferences = preferences with { DisplayMode = "floating" };
            Show(); ApplyAppearance(); ShowToolbar(); SavePreferences();
            log.Write("displayModeChanged", new { mode, mediaConnectionReused = ams != null });
            return;
        }
        if (taskbarHost != null) return;
        SavePreferences(); switchingView = true;
        var host = new TaskbarHost(log, Dispatcher, () => preferences);
        taskbarHost = host;
        host.FloatingRequested += () => _ = SetDisplayModeAsync("floating");
        host.ControlRequested += action => _ = SendAsync(action);
        host.SettingsRequested += ShowSettingsMenu;
        host.FontChangeRequested += delta =>
        { preferences = (preferences with { TaskbarFontSize = preferences.TaskbarFontSize + delta }).Normalize(); ApplyAppearance(); SavePreferences(); };
        host.Unavailable += error => { if (!switchingView && ReferenceEquals(taskbarHost, host)) { source.ToolTip = error; _ = SetDisplayModeAsync("floating"); } };
        host.Update(last, previousLine, lyric.Fill, busy || demo);
        try
        {
            if (await host.StartAsync() && !closing)
            {
                host.SetLocked(locked); preferences = preferences with { DisplayMode = "taskbar" };
                Hide(); SavePreferences(); log.Write("displayModeChanged", new { mode = "taskbar", mediaConnectionReused = ams != null });
            }
            else
            {
                taskbarHost = null; host.Dispose(); preferences = preferences with { DisplayMode = "floating" };
                if (!closing) { Show(); ShowToolbar(); source.Text = "任务栏暂不可用，已保留桌面浮窗"; SavePreferences(); }
            }
        }
        finally { switchingView = false; }
    }
    private async Task ChangeTaskbarMonitorAsync(int index)
    {
        if (switchingView || closing) return;
        await SetDisplayModeAsync("floating"); preferences = preferences with { TaskbarMonitor = index };
        await SetDisplayModeAsync("taskbar");
    }
    private async Task RestoreFloatingPositionAsync()
    {
        await SetDisplayModeAsync("floating"); if (closing) return;
        SetLocked(false); PlaceWindow(reset: true); SavePreferences(); ShowToolbar();
    }

    private async Task ConnectAsync()
    {
        if (busy || closing || demo) return;
        busy = true; SetControls(false); source.Text = "正在连接手机…";
        if (last == null) lyric.Text = "等待手机歌词…";
        await backendGate.WaitAsync();
        try
        {
            var previousConnection = ams;
            var connected = await Task.Run(() => AmsSession.ConnectAsync(log, boundDevice));
            if (closing) { await connected.DisposeAsync(); return; }
            ams = connected; boundDevice = connected.Snapshot.Device;
            ams.Changed += OnState; ShowState(ams.Snapshot);
            if (previousConnection != null) { previousConnection.Changed -= OnState; await previousConnection.DisposeAsync(); }
        }
        catch (Exception ex)
        {
            if (!closing)
            {
                if (ams != null) ShowState(ams.Snapshot);
                else { lyric.Text = "手机尚未连接"; position.Text = "—:—"; }
                source.Text = "请先连接手机，再点“重新连接”"; source.ToolTip = ex.Message;
                ShowToolbar();
            }
            log.Write("overlayConnectFailed", new { error = ex.Message });
        }
        finally { busy = false; backendGate.Release(); UpdateControls(); }
    }
    private void OnState(AmsSnapshot state)
    {
        if (closing) return;
        Dispatcher.BeginInvoke(() => { if (!closing) ShowState(state); });
    }
    private void ShowState(AmsSnapshot state)
    {
        last = state;
        var newContext = $"{state.Player}\u001f{state.Artist}\u001f{state.Album}";
        var contextChanged = newContext != trackContext;
        if (contextChanged) { previousLine = ""; previousLyric.Text = ""; trackContext = newContext; }
        var text = state.PrimaryText.Length == 0 ? "等待手机歌词…" : state.PrimaryText;
        if (lyric.Text != text)
        {
            if (!contextChanged && !string.IsNullOrEmpty(lyric.Text) && !lyric.Text.Contains("等待") && !lyric.Text.Contains("未连接")) previousLine = lyric.Text;
            previousLyric.Text = previousLine;
            lyric.Text = text; lyric.ToolTip = text;
            lyric.BeginAnimation(OpacityProperty, new DoubleAnimation(preferences.TextOpacity * .5, preferences.TextOpacity,
                TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        }
        source.Text = state.Connected ? $"{state.Player} · {state.Artist}" : "手机连接已断开，请重新连接";
        source.ToolTip = state.UnavailableReason ?? $"{state.Player} · {state.Artist}";
        playPause.Content = state.Playback?.State == 0 ? "▶" : state.Playback?.State == 1 ? "❚❚" : "▶";
        var name = state.Playback?.State == 0 ? "播放" : "暂停";
        AutomationProperties.SetName(playPause, name); playPause.ToolTip = name;
        if (!state.Connected) { lyric.Opacity = preferences.TextOpacity * .55; ShowToolbar(); }
        UpdateControls(); RenderPosition();
        taskbarHost?.Update(state, previousLine, lyric.Fill, busy || demo);
    }
    private void RenderPosition()
    {
        var now = AmsSession.MonotonicSeconds;
        var value = last?.PositionAt(now);
        position.Text = value == null ? "—:—" : $"{FormatTime(value.Value)} / {(last!.Duration == null ? "—:—" : FormatTime(last.Duration.Value))}";
        var known = value != null && last?.Duration is > 0;
        progress.Visibility = known ? Visibility.Visible : Visibility.Hidden;
        progress.Value = known ? Math.Clamp(value!.Value / last!.Duration!.Value, 0, 1) : 0;
        taskbarHost?.UpdatePosition(last);
    }
    private static string FormatTime(double value) => $"{(int)(value / 60)}:{(int)value % 60:00}";
    private void SetControls(bool enabled) => previous.IsEnabled = playPause.IsEnabled = next.IsEnabled = enabled;
    private void UpdateControls()
    {
        taskbarHost?.Update(last, previousLine, lyric.Fill, busy || demo);
        var state = last;
        if (demo || busy || closing || state is not { Connected: true } || string.IsNullOrEmpty(state.Player)) { SetControls(false); return; }
        bool Supports(int command) => state.SupportedCommands.Length == 0 || state.SupportedCommands.Contains(command);
        previous.IsEnabled = Supports(4); next.IsEnabled = Supports(3);
        playPause.IsEnabled = state.Playback?.State is 0 or 1 && Supports(state.Playback.State == 0 ? 0 : 1);
    }
    private async Task SendAsync(string action)
    {
        var expectedPlayer = last?.Player;
        if (ams == null || string.IsNullOrWhiteSpace(expectedPlayer) || busy || closing || demo) return;
        busy = true; SetControls(false); await backendGate.WaitAsync();
        try { await ams.SendAsync(action, expectedPlayer, allowUnknownCapabilities: true); }
        catch (Exception ex) { if (!closing) { source.Text = "操作暂未完成，请重试"; source.ToolTip = ex.Message; } log.Write("overlayControlFailed", new { action, error = ex.Message }); }
        finally { busy = false; backendGate.Release(); UpdateControls(); }
    }
    private async Task SavePreviewAsync()
    {
        void Save(string name)
        {
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(log.DirectoryPath, name); using var stream = File.Create(path); encoder.Save(stream); log.Write("overlayPreview", new { path });
        }
        lyric.BeginAnimation(OpacityProperty, null);
        HideToolbar(); await Task.Delay(80); if (closing) return; Save("lyrics-transparent.png");
        ShowToolbar(); await Task.Delay(80); if (closing) return; Save("lyrics-toolbar.png"); hideToolbar.Start();
    }
}
