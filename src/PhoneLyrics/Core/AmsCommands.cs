namespace PhoneLyrics;

internal static class AmsCommands
{
    public static byte Id(string action) => action switch
    {
        "play" => 0,
        "pause" => 1,
        "toggle" => 2,
        "next" => 3,
        "previous" => 4,
        "volume-up" => 5,
        "volume-down" => 6,
        _ => throw new ArgumentException($"Unsupported AMS command: {action}")
    };
}
