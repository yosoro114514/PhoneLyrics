using System.Globalization;

namespace PhoneLyrics;

internal sealed record DesktopOptions(string? DeviceName, string? Mode, bool Demo, bool Preview, bool RegisterHotKey)
{
    public static DesktopOptions Parse(string[] args)
    {
        string? device = null, mode = null;
        var demo = false;
        var preview = false;
        var hotKey = true;
        for (var i = args.FirstOrDefault() == "overlay" ? 1 : 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--device-name": device = ArgumentValues.Next(args, ref i); break;
                case "--mode": mode = ArgumentValues.Next(args, ref i); break;
                case "--demo": demo = true; break;
                case "--preview": preview = true; break;
                case "--no-hotkey": hotKey = false; break;
                default: throw new ArgumentException($"Invalid desktop argument: {args[i]}");
            }
        }
        if (mode is not null and not "floating" and not "taskbar")
            throw new ArgumentException("--mode requires floating or taskbar");
        return new(device, mode, demo, preview, hotKey);
    }
}

internal sealed record CommandOptions(string Command, int Seconds, string? OutputDirectory, string? Action,
    string? ExpectedPlayer, string? DeviceName)
{
    public static CommandOptions Parse(string[] args)
    {
        var command = args.FirstOrDefault() ?? "help";
        if (command is "--help" or "-h") command = "help";
        if (command is not "help" and not "ams-discover" and not "ams-watch" and not "ams-control" and not "taskbar-layout")
            throw new ArgumentException($"Unknown command: {command}. Use help for AMS commands.");
        var seconds = 60;
        string? output = null, action = null, player = null, device = null;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seconds":
                    if (!int.TryParse(ArgumentValues.Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds)
                        || seconds is < 1 or > 3600)
                        throw new ArgumentException("--seconds must be between 1 and 3600");
                    break;
                case "--out": output = ArgumentValues.Next(args, ref i); break;
                case "--expect-player": player = ArgumentValues.Next(args, ref i); break;
                case "--device-name": device = ArgumentValues.Next(args, ref i); break;
                default:
                    if (command == "ams-control" && action == null && !args[i].StartsWith('-')) action = args[i];
                    else throw new ArgumentException($"Invalid argument: {args[i]}");
                    break;
            }
        }
        // Validate control intent before opening a Bluetooth session.
        if (command == "ams-control")
        {
            if (action == null || player == null) throw new ArgumentException("ams-control requires an action and --expect-player");
            _ = AmsCommands.Id(action);
        }
        else if (player != null) throw new ArgumentException("--expect-player applies only to ams-control");
        return new(command, seconds, output, action, player, device);
    }
}

internal static class ArgumentValues
{
    public static string Next(string[] args, ref int index)
    {
        var option = args[index];
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} requires a value");
        return args[++index];
    }
}
