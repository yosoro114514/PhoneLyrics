using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PhoneLyrics;

internal sealed class TraceLog : IDisposable
{
    private readonly object gate = new();
    private readonly StreamWriter writer;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private bool disposed;
    public string DirectoryPath { get; }
    public string TracePath => Path.Combine(DirectoryPath, "trace.jsonl");
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public TraceLog(string? output)
    {
        DirectoryPath = Path.GetFullPath(output ?? Path.Combine(AppStorage.DesktopMode ? AppStorage.Root : Environment.CurrentDirectory, "diagnostics",
            $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}"));
        Directory.CreateDirectory(DirectoryPath);
        writer = new StreamWriter(new FileStream(TracePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        Write("run", new { runId = Guid.NewGuid(), formatVersion = 1, readOnlyByDefault = true,
            privacy = "Local AMS media metadata and taskbar geometry; no notification/chat contents; no network upload" });
    }

    public void Write(string kind, object data)
    {
        lock (gate)
        {
            if (disposed) return;
            writer.WriteLine(JsonSerializer.Serialize(new { kind, utc = DateTimeOffset.UtcNow,
                monotonicSeconds = stopwatch.Elapsed.TotalSeconds, data }, JsonOptions));
        }
    }
    public void Dispose()
    {
        lock (gate) { disposed = true; writer.Dispose(); }
    }
}

internal static class EnvironmentInfo
{
    public static object Read() => new
    {
        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        processSession = Process.GetCurrentProcess().SessionId,
        interactive = Environment.UserInteractive,
        desktopSessions = ReadProcesses("explorer"),
        phoneLinkProcesses = ReadProcesses("PhoneExperienceHost")
    };
    private static object[] ReadProcesses(string name) => Process.GetProcessesByName(name).Select(p =>
    {
        using (p) return (object)new { pid = p.Id, session = p.SessionId, process = p.ProcessName };
    }).ToArray();
}
