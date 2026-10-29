using System.IO.Pipes;

namespace PowerPlan.App;

/// <summary>
/// One tray app per user. A second launch (Start menu, login item, installer) asks the running app to show its window
/// and exits, instead of adding a second tray icon and a second sleep catcher.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // One per data directory, so a demo or test copy (NAPPILOT_DATA_DIR) can run beside the real one.
    static readonly string Name = $"NapPilot-{Environment.UserName}-" +
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PowerPlan.Core.AppPaths.DataDirectory)))[..12];

    readonly Mutex mutex;
    readonly CancellationTokenSource stop = new();

    SingleInstance(Mutex mutex) => this.mutex = mutex;

    /// <summary>Raised on a background thread when another launch asks for the window.</summary>
    public event Action? ShowRequested;

    /// <summary>Returns null if the app is already running, after asking it to show its window.</summary>
    public static SingleInstance? TryClaim()
    {
        var mutex = new Mutex(initiallyOwned: true, Name, out var created);
        if (!created)
        {
            mutex.Dispose();
            NotifyRunningInstance();
            return null;
        }
        var instance = new SingleInstance(mutex);
        _ = Task.Run(instance.ListenAsync);
        return instance;
    }

    static void NotifyRunningInstance()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(TimeSpan.FromSeconds(2));
            pipe.WriteByte(1);
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            // The running app is starting up or shutting down; nothing more to do.
        }
    }

    async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(stop.Token);
                if (pipe.ReadByte() == 1) ShowRequested?.Invoke();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that disconnects early; keep listening.
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
