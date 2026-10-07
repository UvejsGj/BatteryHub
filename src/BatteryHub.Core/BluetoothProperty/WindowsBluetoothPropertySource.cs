using BatteryHub.Core.Windows;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BatteryHub.Core.BluetoothProperty;

/// <summary>
/// Device nodes through CfgMgr32 (a sweep of the BTHENUM and BTHLE enumerators takes about a millisecond) and paired
/// devices through WinRT association endpoints. The paired-device selectors read Windows' records: no radio scan,
/// no connection.
/// </summary>
internal sealed class WindowsBluetoothPropertySource : IBluetoothPropertySource
{
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string ContainerProperty = "System.Devices.Aep.ContainerId";
    private const string AppearanceProperty = "System.Devices.Aep.Bluetooth.Le.Appearance";

    // Paired-device queries answer in tens of milliseconds; a stuck Bluetooth service should not hold a poll.
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);
        try
        {
            var classic = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                [AddressProperty, ConnectedProperty, ContainerProperty],
                DeviceInformationKind.AssociationEndpoint).AsTask(timeout.Token).ConfigureAwait(false);
            var lowEnergy = await DeviceInformation.FindAllAsync(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                [AddressProperty, ConnectedProperty, ContainerProperty, AppearanceProperty],
                DeviceInformationKind.AssociationEndpoint).AsTask(timeout.Token).ConfigureAwait(false);

            return
            [
                .. classic.Select(info => ToEndpoint(info, lowEnergy: false)).OfType<BluetoothEndpoint>(),
                .. lowEnergy.Select(info => ToEndpoint(info, lowEnergy: true)).OfType<BluetoothEndpoint>(),
            ];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Windows did not list paired Bluetooth devices within {QueryTimeout.TotalSeconds:0} s.");
        }
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
        if (!(BluetoothAddress.TryParse(addressText, out ulong address) && address != 0)
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

    private static BluetoothEndpoint? ToEndpoint(DeviceInformation info, bool lowEnergy)
    {
        if (!info.Properties.TryGetValue(AddressProperty, out object? addressValue)
            || !BluetoothAddress.TryParse(addressValue as string, out ulong address))
        {
            return null;
        }

        bool connected = info.Properties.TryGetValue(ConnectedProperty, out object? connectedValue) && connectedValue is true;
        Guid? container = info.Properties.TryGetValue(ContainerProperty, out object? containerValue) && containerValue is Guid id ? id : null;
        ushort? appearance = info.Properties.TryGetValue(AppearanceProperty, out object? appearanceValue) && appearanceValue is ushort raw && raw != 0
            ? raw
            : null;
        return new BluetoothEndpoint(address, lowEnergy, info.Name, connected, container, appearance);
    }
}
