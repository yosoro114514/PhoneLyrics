using System.Globalization;
using System.Text;

namespace PhoneLyrics;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        var desktop = args.Length == 0 || args[0] == "overlay";
        try
        {
            if (desktop) return DesktopApplication.Run(DesktopOptions.Parse(args));
            Console.OutputEncoding = Encoding.UTF8;
            return AmsDiagnostics.RunAsync(CommandOptions.Parse(args)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            if (desktop)
                System.Windows.MessageBox.Show($"PhoneLyrics 未能启动：\n{ex.Message}", "PhoneLyrics",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            else
                Console.Error.WriteLine($"FAILED {ex.GetType().Name}: {ex.Message} (0x{ex.HResult:X8})");
            return 1;
        }
    }
}
