using System.Text.Json;

namespace PhoneLyrics;

internal sealed record OverlayPreferences(string Theme = "gold", double FontSize = 46, double Width = 1000,
    bool TwoLines = false, double TextOpacity = 1, double? Left = null, double? Top = null,
    string DisplayMode = "floating", double TaskbarFontSize = 18, double TaskbarWidth = 600, bool TaskbarTwoLines = true, int TaskbarMonitor = 0)
{
    public static string FilePath => AppStorage.SettingsFile;
    public static OverlayPreferences Read()
    {
        try
        {
            if (new FileInfo(FilePath) is { Exists: true, Length: < 65536 })
                return (JsonSerializer.Deserialize<OverlayPreferences>(File.ReadAllText(FilePath), TraceLog.JsonOptions) ?? new()).Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    public OverlayPreferences Normalize() => this with
    {
        Theme = Theme is "gold" or "white" or "mint" ? Theme : "gold",
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 26, 72) : 46,
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 720, 1600) : 1000,
        TextOpacity = double.IsFinite(TextOpacity) ? Math.Clamp(TextOpacity, .4, 1) : 1,
        Left = Left is { } left && double.IsFinite(left) ? left : null,
        Top = Top is { } top && double.IsFinite(top) ? top : null,
        DisplayMode = DisplayMode is "floating" or "taskbar" ? DisplayMode : "floating",
        TaskbarFontSize = double.IsFinite(TaskbarFontSize) ? Math.Clamp(TaskbarFontSize, 12, 26) : 18,
        TaskbarWidth = double.IsFinite(TaskbarWidth) ? Math.Clamp(TaskbarWidth, 220, 1000) : 600,
        TaskbarMonitor = Math.Clamp(TaskbarMonitor, 0, 7)
    };
    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(Normalize(), TraceLog.JsonOptions));
}
