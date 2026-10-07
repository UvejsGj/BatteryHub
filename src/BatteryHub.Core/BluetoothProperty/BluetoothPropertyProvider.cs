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

        // A dual-mode device can have a classic and an LE node with the same address.
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
        var now = _time.GetUtcNow();
        return nodes.Select(node => Inspect(node, Match(node, endpoints), now)).ToList();
    }

    public void Dispose() => _disposed = true;

    /// <summary>The paired device a node belongs to: same transport, same address, else the same container.</summary>
    internal static BluetoothEndpoint? Match(BluetoothBatteryNode node, IReadOnlyList<BluetoothEndpoint> endpoints)
    {
        bool lowEnergy = node.Kind == BluetoothNodeKind.LowEnergy;
        var sameTransport = endpoints.Where(e => e.IsLowEnergy == lowEnergy).ToList();
        return sameTransport.FirstOrDefault(e => e.Address == node.Address)
            ?? (node.ContainerId is { } container ? sameTransport.FirstOrDefault(e => e.ContainerId == container) : null);
    }

    private static BluetoothPropertyInspection Inspect(BluetoothBatteryNode node, BluetoothEndpoint? endpoint, DateTimeOffset now)
    {
        if (endpoint is null)
        {
            return new(node, null, null, "Windows lists no paired device for this node; not shown");
        }

        if (!endpoint.IsConnected)
        {
            return new(node, endpoint, null, "not connected; Windows keeps the last level after a disconnect, so it is not shown");
        }

        var reading = new BatteryReading
        {
            DeviceId = DeviceIds.Bluetooth(node.Address),
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
