namespace BatteryHub.Core.BluetoothProperty;

/// <summary>What one poll learned about one device node. The Probe prints it.</summary>
/// <param name="Endpoint">The paired device the node belongs to, or null when Windows lists none.</param>
/// <param name="Note">Why there is no reading, or what the reading means.</param>
public sealed record BluetoothPropertyInspection(
    BluetoothBatteryNode Node,
    BluetoothEndpoint? Endpoint,
    BatteryReading? Reading,
    string? Note);

/// <summary>
/// The battery level Windows itself stores for Bluetooth devices, the number Settings shows: classic headsets that
/// report it over the hands-free profile (HFP), and LE devices with a Battery Service, including keyboards, mice and
/// pads whose GATT service apps may not read. Polled every 60 seconds.
/// </summary>
/// <remarks>
/// Windows keeps the last value after a device disconnects (for months, in reports), so a value is shown only while
/// Windows lists the device as connected. The value is whatever the device last sent; many headsets send it only
/// when they connect. Classic Bluetooth carries no charging state.
/// </remarks>
public sealed class BluetoothPropertyProvider : IBatteryProvider
{
    public const string ProviderName = "bt-property";

    private readonly IBluetoothPropertySource _source;
    private readonly TimeProvider _time;
    private volatile bool _disposed;

    public BluetoothPropertyProvider(TimeProvider? time = null)
        : this(new WindowsBluetoothPropertySource(), time)
    {
    }

    internal BluetoothPropertyProvider(IBluetoothPropertySource source, TimeProvider? time = null)
    {
        _source = source;
        _time = time ?? TimeProvider.System;
    }

    public string Name => ProviderName;

    public TimeSpan PollInterval { get; } = TimeSpan.FromSeconds(60);

    // Windows raises no event when it rewrites the value; polling covers it.
    public event EventHandler<ReadingsChangedEventArgs>? ReadingsChanged
    {
        add { }
        remove { }
    }

    public async Task<IReadOnlyList<BatteryReading>> PollAsync(CancellationToken cancellationToken)
    {
        var inspections = await InspectAsync(cancellationToken).ConfigureAwait(false);

        // One device can yield two readings with the same ID: a dual-mode device's hands-free and LE nodes.
        return ReadingDeduplicator.Merge(inspections
            .Select(i => i.Reading)
            .OfType<BatteryReading>()
            .Select(r => (IReadOnlyList<BatteryReading>)[r]));
    }

    /// <summary>Reads every hands-free and LE node once. <see cref="PollAsync"/> and the Probe both use this.</summary>
    public async Task<IReadOnlyList<BluetoothPropertyInspection>> InspectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var nodes = await Task.Run(_source.ListNodes, cancellationToken).ConfigureAwait(false);
        if (nodes.Count == 0)
        {
            return [];
        }

        var endpoints = await _source.ListPairedAsync(cancellationToken).ConfigureAwait(false);
        var classic = endpoints.Where(e => !e.IsLowEnergy).Select(e => (e.Address, e.ContainerId)).ToList();
        var now = _time.GetUtcNow();
        return nodes.Select(node => Inspect(node, Match(node, endpoints), classic, now)).ToList();
    }

    public void Dispose() => _disposed = true;

    /// <summary>The paired device a node belongs to: same transport, same address.</summary>
    /// <remarks>
    /// Never matched by container alone: the connection state would then be another node's (e.g. the other earbud's),
    /// and a level Windows kept after a disconnect would show as current.
    /// </remarks>
    internal static BluetoothEndpoint? Match(BluetoothBatteryNode node, IReadOnlyList<BluetoothEndpoint> endpoints) =>
        endpoints.FirstOrDefault(e => e.IsLowEnergy == (node.Kind == BluetoothNodeKind.LowEnergy) && e.Address == node.Address);

    private static BluetoothPropertyInspection Inspect(
        BluetoothBatteryNode node,
        BluetoothEndpoint? endpoint,
        IReadOnlyList<(ulong Address, Guid? Container)> classic,
        DateTimeOffset now)
    {
        if (endpoint is null)
        {
            return new(node, null, null, "Windows lists no paired device for this node; not shown");
        }

        if (!endpoint.IsConnected)
        {
            return new(node, endpoint, null, "not connected; Windows keeps the last level after a disconnect, so it is not shown");
        }

        // An LE node of a dual-mode device is keyed by the device's classic address, as the other readers key it.
        ulong idAddress = node.Kind == BluetoothNodeKind.LowEnergy
            ? BluetoothIdentity.ClassicAddressInContainer(endpoint.ContainerId ?? node.ContainerId, classic) ?? node.Address
            : node.Address;
        var reading = new BatteryReading
        {
            DeviceId = DeviceIds.Bluetooth(idAddress),
            Name = NameFor(node, endpoint),
            Kind = node.Kind == BluetoothNodeKind.HandsFree ? DeviceKind.Headset : Ble.BleBattery.KindFromAppearance(endpoint.Appearance),
            Connection = node.Kind == BluetoothNodeKind.HandsFree ? ConnectionType.Bluetooth : ConnectionType.Ble,
            Timestamp = now,
            Source = ProviderName,
        };

        if (node.Percent is { } percent)
        {
            return new(node, endpoint, reading with { Percent = percent }, null);
        }

        // A connected headset with no value is worth a row (it shows the headset was seen); an LE device without
        // one is left to the GATT reader, which tells "no battery service" apart from "not reported yet".
        return node.Kind == BluetoothNodeKind.HandsFree
            ? new(node, endpoint, reading with
            {
                Status = ReadingStatus.NoData,
                StatusDetail = "Windows has no battery level for this headset",
            }, "connected, no stored level")
            : new(node, endpoint, null, "connected, no stored level; not shown");
    }

    private static string NameFor(BluetoothBatteryNode node, BluetoothEndpoint endpoint) =>
        !string.IsNullOrWhiteSpace(endpoint.Name) ? endpoint.Name
        : BluetoothNodeId.NameFromFriendlyName(node.FriendlyName) ?? DeviceIds.Bluetooth(node.Address);
}
