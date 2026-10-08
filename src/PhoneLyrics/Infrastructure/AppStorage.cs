namespace PhoneLyrics;

internal static class AppStorage
{
    public static string Root { get; private set; } = Environment.CurrentDirectory;
    public static bool DesktopMode { get; private set; }
    public static string SettingsFile => Path.Combine(Root, "overlay-settings.json");

    public static void InitializeDesktop()
    {
        DesktopMode = true;
        var portable = AppContext.BaseDirectory;
        var probe = Path.Combine(portable, $".phone-lyrics-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
            Root = portable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneLyrics");
            Directory.CreateDirectory(Root);
        }
    }
}
