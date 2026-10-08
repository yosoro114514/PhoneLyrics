using System.Text.Json;

namespace PhoneLyrics;

internal static class AmsDiagnostics
{
    public static async Task<int> RunAsync(CommandOptions options)
    {
        if (options.Command == "help")
        {
            Console.WriteLine("""
                PhoneLyrics — iPhone AMS 桌面 / 任务栏歌词
                无参数启动歌词界面；终端诊断建议使用控制台发布版。

                overlay [--mode floating|taskbar] [--device-name "iPhone 名称"]
                overlay --demo        原创示例文字；不连接或控制手机
                ams-discover          探测已配对设备的 AMS 服务（不控制播放）
                ams-watch --seconds 60
                                      收集媒体通知（不控制播放，Ctrl+C 停止）
                ams-control play --expect-player "网易云音乐" --seconds 10
                                      明确控制手机；支持 play / pause / toggle /
                                      next / previous / volume-up / volume-down
                taskbar-layout        只读测量任务栏控件范围
                help                  查看说明

                诊断可选：--device-name "已配对设备名称"；--out "日志目录"
                默认在当前目录 diagnostics 下保存。多个 BLE 设备时须指定名称。
                进度只读；未实现拖动定位、自动重连或完整 LRC 同步。
                """);
            return 0;
        }
        using var log = new TraceLog(options.OutputDirectory);
        log.Write("environment", EnvironmentInfo.Read());
        Console.WriteLine($"日志：{log.DirectoryPath}");
        if (options.Command == "ams-discover")
        {
            await AmsDiscovery.DiscoverAsync(log, options.DeviceName);
            return 0;
        }
        if (options.Command == "taskbar-layout")
        {
            var layout = await Task.Run(() => TaskbarLayoutProbe.Read());
            var result = new { handle = layout.Handle.ToInt64(), layout.Left, layout.Top, layout.Width, layout.Height,
                layout.Dpi, layout.Occupied, suggested = TaskbarGeometry.ChooseGap(layout.Width, layout.Occupied, 660, 240) };
            log.Write("taskbarLayout", result);
            Console.WriteLine(JsonSerializer.Serialize(result, TraceLog.JsonOptions));
            return 0;
        }
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            await using var ams = await AmsSession.ConnectAsync(log, options.DeviceName);
            ams.Changed += state => Console.WriteLine(JsonSerializer.Serialize(state, TraceLog.JsonOptions));
            Console.WriteLine(JsonSerializer.Serialize(ams.Snapshot, TraceLog.JsonOptions));
            if (options.Command == "ams-control")
            {
                await Task.Delay(1500, cancel.Token);
                await ams.SendAsync(options.Action!, options.ExpectedPlayer!, allowUnknownCapabilities: true);
                Console.WriteLine("AMS 已交付命令；手机实际响应仍需手机侧观察确认。");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Seconds), cancel.Token); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            log.Write("amsWatchComplete", new { periodicGattReads = 0, finalState = ams.Snapshot });
            return 0;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return 0; }
        finally { Console.CancelKeyPress -= onCancel; }
    }
}
