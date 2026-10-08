using System.Diagnostics;
using System.Security.Principal;

namespace PhoneLyrics;

// Scoped to this Windows user and desktop session; carries only a request to reveal the UI.
internal sealed class DesktopInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle? activation;
    private RegisteredWaitHandle? listener;
    public bool IsPrimary { get; }

    public DesktopInstance()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        var scope = $"Local\\PhoneLyrics-{identity.User?.Value}-{process.SessionId}";
        mutex = new Mutex(false, scope + "-instance");
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (IsPrimary)
        {
            activation = new EventWaitHandle(false, EventResetMode.AutoReset, scope + "-activate");
        }
        else
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try { using var existing = EventWaitHandle.OpenExisting(scope + "-activate"); existing.Set(); break; }
                catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(50); }
            }
        }
    }

    public void Listen(Action reveal)
    {
        if (activation != null)
            listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => reveal(), null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        listener?.Unregister(null);
        activation?.Dispose();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
