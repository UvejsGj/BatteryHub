using BatteryHub.Core.BluetoothProperty;

namespace BatteryHub.Tests.BluetoothProperty;

internal sealed class FakeBluetoothPropertySource : IBluetoothPropertySource
{
    public List<BluetoothBatteryNode> Nodes { get; } = [];

    public List<BluetoothEndpoint> Endpoints { get; } = [];

    public Exception? PairedError { get; set; }

    /// <summary>When set, ListPairedAsync waits for it, to hold a poll mid-way.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public int PairedQueries;

    public int InFlight;

    public int MaxInFlight;

    public bool Disposed { get; private set; }

    public event EventHandler? DevicesChanged;

    public void RaiseDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);

    public IReadOnlyList<BluetoothBatteryNode> ListNodes() => Nodes.ToList();

    public async Task<IReadOnlyList<BluetoothEndpoint>> ListPairedAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref PairedQueries);
        int now = Interlocked.Increment(ref InFlight);
        MaxInFlight = Math.Max(MaxInFlight, now);
        try
        {
            if (Hold is { } hold)
            {
                await hold.Task.WaitAsync(cancellationToken);
            }

            return PairedError is { } error ? throw error : Endpoints.ToList();
        }
        finally
        {
            Interlocked.Decrement(ref InFlight);
        }
    }

    public void Dispose() => Disposed = true;
}
