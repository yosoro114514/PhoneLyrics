using System.Windows;

namespace PhoneLyrics;

internal static class DesktopApplication
{
    public static int Run(DesktopOptions options)
    {
        using var instance = options.Demo ? null : new DesktopInstance();
        if (instance is { IsPrimary: false }) return 0;
        AppStorage.InitializeDesktop();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new PhoneOverlay(options.Preview, options.DeviceName, options.Demo, options.RegisterHotKey, options.Mode);
        instance?.Listen(() =>
        {
            if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(window.Reveal);
        });
        return app.Run(window);
    }
}
