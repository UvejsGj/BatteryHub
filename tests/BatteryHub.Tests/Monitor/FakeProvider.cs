using BatteryHub.Core;

namespace BatteryHub.Tests.Monitor;

/// <param name="results">What successive polls return; the last one repeats. A null entry makes that poll throw.</param>
internal sealed class FakeProvider(string name, TimeSpan interval, params IReadOnlyList<BatteryReading>?[] results) : IBatteryProvider
{
    private int _polls;

    public string Name { get; } = name;

    public TimeSpan PollInterval { get; } = interval;

    public int Polls => Volatile.Read(ref _polls);

    public bool Disposed { get; private set; }

    /// <summary>When set, a poll waits for it before answering.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public event EventHandler<ReadingsChangedEventArgs>? ReadingsChanged;

    public bool HasPushSubscribers => ReadingsChanged is not null;

    public async Task<IReadOnlyList<BatteryReading>> PollAsync(CancellationToken cancellationToken)
    {
        int index = Interlocked.Increment(ref _polls) - 1;
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        var result = results.Length == 0 ? [] : results[Math.Min(index, results.Length - 1)];
        return result ?? throw new InvalidOperationException($"{Name} failed");
    }

    public void Push(params BatteryReading[] readings) => ReadingsChanged?.Invoke(this, new ReadingsChangedEventArgs(readings));

    public void Dispose() => Disposed = true;
}

internal static class Readings
{
    public static BatteryReading Of(string id, string name, int? percent, string source = "fake") => new()
    {
        DeviceId = id,
        Name = name,
        Kind = DeviceKind.Gamepad,
        Connection = ConnectionType.Usb,
        Percent = percent,
        Timestamp = DateTimeOffset.UnixEpoch,
        Source = source,
    };
}
