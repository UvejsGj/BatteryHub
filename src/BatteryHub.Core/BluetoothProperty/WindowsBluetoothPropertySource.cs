using BatteryHub.Core.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryHub.Core.BluetoothProperty;

/// <summary>
/// Device nodes through CfgMgr32 (a sweep of the BTHENUM and BTHLE enumerators takes about a millisecond) and paired
/// devices through watchers over Windows' association-endpoint records, which read Windows' records: no radio scan,
/// no connection.
/// </summary>
internal sealed class WindowsBluetoothPropertySource : IBluetoothPropertySource
{
    private readonly PairedDeviceWatcher _classic;
    private readonly PairedDeviceWatcher _lowEnergy;

    public WindowsBluetoothPropertySource(ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        _classic = PairedDeviceWatcher.Classic(log);
        _lowEnergy = PairedDeviceWatcher.LowEnergy(log);
        _classic.Changed += OnChanged;
        _lowEnergy.Changed += OnChanged;
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<BluetoothBatteryNode> ListNodes()
    {
        var nodes = new List<BluetoothBatteryNode>();
        foreach (string enumerator in (string[])[BluetoothPropertyConstants.ClassicEnumerator, BluetoothPropertyConstants.LowEnergyEnumerator])
        {
            foreach (string instanceId in DeviceNodes.ListPresent(enumerator))
            {
                if (BluetoothNodeId.Classify(instanceId) is { } kind && ReadNode(instanceId, kind) is { } node)
                {
                    nodes.Add(node);
                }
            }
        }

        return nodes;
    }

    public async Task<IReadOnlyList<BluetoothEndpoint>> ListPairedAsync(CancellationToken cancellationToken)
    {
        var classic = await _classic.ListAsync(cancellationToken).ConfigureAwait(false);
        var lowEnergy = await _lowEnergy.ListAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. classic.Select(d => new BluetoothEndpoint(d.Address, false, d.Name, d.IsConnected, d.ContainerId, null)),
            .. lowEnergy.Select(d => new BluetoothEndpoint(d.Address, true, d.Name, d.IsConnected, d.ContainerId, d.Appearance)),
        ];
    }

    public void Dispose()
    {
        _classic.Changed -= OnChanged;
        _lowEnergy.Changed -= OnChanged;
        _classic.Dispose();
        _lowEnergy.Dispose();
    }

    /// <summary>Null when the node went away since it was listed, or carries no remote address.</summary>
    internal static BluetoothBatteryNode? ReadNode(string instanceId, BluetoothNodeKind kind)
    {
        if (!DeviceNodes.TryLocate(instanceId, out uint node))
        {
            return null;
        }

        // The address property is documented; the instance ID layout is not, so it is only the fallback.
        string? addressText = DeviceNodes.Read(node, BluetoothPropertyConstants.DeviceAddress).AsString();
        if (!BluetoothAddress.TryParse(addressText, out ulong address)
            && !BluetoothNodeId.TryGetAddress(instanceId, out address))
        {
            return null;
        }

        var battery = DeviceNodes.Read(node, BluetoothPropertyConstants.Battery);
        return new BluetoothBatteryNode(
            instanceId,
            kind,
            address,
            DeviceNodes.Read(node, DeviceNodes.FriendlyName).AsString(),
            DeviceNodes.Read(node, DeviceNodes.ContainerId).AsGuid(),
            BluetoothNodeId.Percent(battery),
            battery.Describe());
    }

    private void OnChanged(object? sender, EventArgs e) => DevicesChanged?.Invoke(this, e);
}
