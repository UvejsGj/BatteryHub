namespace BatteryHub.App;

/// <summary>
/// Keeps one copy of the app per Windows session. A second launch asks the running copy to open its flyout and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\BatteryHub.Instance";
    private const string ShowEventName = @"Local\BatteryHub.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showWait;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Raised on a thread-pool thread when another launch asks this copy to show itself.</summary>
    public event Action? ShowRequested;

    /// <summary>Returns null when another copy is already running.</summary>
    public static SingleInstance? TryClaim()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public static void ShowRunningInstance()
    {
        if (EventWaitHandle.TryOpenExisting(ShowEventName, out var showEvent))
        {
            using (showEvent)
            {
                // This launch came from the user, so it may hand foreground rights on; without them the running
                // copy's flyout opens unfocused and does not close when the user clicks elsewhere.
                NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
                showEvent.Set();
            }
        }
    }

    /// <summary>Call on the thread that called <see cref="TryClaim"/>: a mutex is released by the thread that owns it.</summary>
    public void Dispose()
    {
        _showWait.Unregister(null);
        _showEvent.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
