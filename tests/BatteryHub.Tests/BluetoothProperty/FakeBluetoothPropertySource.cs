using BatteryHub.Core.BluetoothProperty;

namespace BatteryHub.Tests.BluetoothProperty;

internal sealed class FakeBluetoothPropertySource : IBluetoothPropertySource
{
    public List<BluetoothBatteryNode> Nodes { get; } = [];

    public List<BluetoothEndpoint> Endpoints { get; } = [];

    public Exception? PairedError { get; set; }

    public int PairedQueries { get; private set; }

    public IReadOnlyList<BluetoothBatteryNode> ListNodes() => Nodes.ToList();

    public Task<IReadOnlyList<BluetoothEndpoint>> ListPairedAsync(CancellationToken cancellationToken)
    {
        PairedQueries++;
        return PairedError is { } error
            ? Task.FromException<IReadOnlyList<BluetoothEndpoint>>(error)
            : Task.FromResult<IReadOnlyList<BluetoothEndpoint>>(Endpoints.ToList());
    }
}
