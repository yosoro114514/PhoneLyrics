using System.Windows.Media.Imaging;

namespace PhoneLyrics;

internal static class AppIcon
{
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("PhoneLyrics.Icon")
            ?? throw new InvalidOperationException("程序图标资源缺失。");
        using var original = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)original.Clone();
    }

    public static BitmapSource CreateWindowIcon()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("PhoneLyrics.Icon")
            ?? throw new InvalidOperationException("程序图标资源缺失。");
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        image.Freeze();
        return image;
    }

    public static string Notices()
    {
        string Read(string name)
        {
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return Read("PhoneLyrics.License") + "\n\n" + Read("PhoneLyrics.Notices")
            + "\n\n.NET runtime\n" + Read("PhoneLyrics.DotNetLicense")
            + "\n\n.NET runtime third-party notices\n" + Read("PhoneLyrics.DotNetNotices")
            + "\n\nWindows Desktop runtime\n" + Read("PhoneLyrics.DesktopRuntimeLicense")
            + "\n\nC#/WinRT\n" + Read("PhoneLyrics.CsWinRTLicense")
            + "\n\nWindows SDK\n" + Read("PhoneLyrics.WindowsSdkLicense");
    }
}
