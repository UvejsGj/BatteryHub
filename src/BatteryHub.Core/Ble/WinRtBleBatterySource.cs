using BatteryHub.Core.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BatteryHub.Core.Ble;

/// <summary>Paired Bluetooth LE devices and their Battery Level, through WinRT.</summary>
/// <remarks>
/// Based on Microsoft's GATT client guidance and the BluetoothLE sample (microsoft/Windows-universal-samples):
/// watchers over paired-device association endpoints keep the device list and connection state current
/// (FindAllAsync on endpoints can take 10 s or more); a read re-checks the connection first, looks the service and
/// characteristic up in Windows' cache (an uncached lookup would start a connection), reads the value from the
/// device, and disposes every WinRT object so BatteryHub never keeps a link alive.
/// </remarks>
internal sealed class WinRtBleBatterySource : IBleBatterySource
{
    // An unreachable device times out after about 7 s per queued request.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly PairedDeviceWatcher _lowEnergy;

    // The classic halves of dual-mode devices, so their LE readings can be keyed by the classic address.
    private readonly PairedDeviceWatcher _classic;

    public WinRtBleBatterySource(ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        _lowEnergy = PairedDeviceWatcher.LowEnergy(log);
        _classic = PairedDeviceWatcher.Classic(log);
        _lowEnergy.Changed += OnLowEnergyChanged;
    }

    public event EventHandler? DevicesChanged;

    public async Task<IReadOnlyList<BleDeviceInfo>> ListPairedAsync(CancellationToken cancellationToken)
    {
        var lowEnergy = await _lowEnergy.ListAsync(cancellationToken).ConfigureAwait(false);
        var classic = (await _classic.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(d => (d.Address, d.ContainerId))
            .ToList();
        return lowEnergy.Select(d =>
        {
            ulong? classicAddress = BluetoothIdentity.ClassicAddressInContainer(d.ContainerId, classic);
            string name = string.IsNullOrWhiteSpace(d.Name) ? DeviceIds.Bluetooth(d.Address) : d.Name;
            return new BleDeviceInfo(d.Id, name, d.Address, d.IsConnected, d.Appearance, classicAddress == d.Address ? null : classicAddress);
        }).ToList();
    }

    public async Task<BleBatteryRead> ReadAsync(BleDeviceInfo device, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        var ct = timeout.Token;

        BluetoothLEDevice? ble = null;
        GattDeviceServicesResult? services = null;
        try
        {
            ble = await BluetoothLEDevice.FromIdAsync(device.Id).AsTask(ct).ConfigureAwait(false);
            if (ble is null || ble.ConnectionStatus != BluetoothConnectionStatus.Connected)
            {
                return new BleBatteryRead(BleReadStatus.Unreachable, null, "not connected");
            }

            services = await ble.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(ct).ConfigureAwait(false);
            if (Failed(services.Status, services.ProtocolError) is { } serviceFailure)
            {
                return serviceFailure;
            }

            if (services.Services.Count == 0)
            {
                return new BleBatteryRead(BleReadStatus.NoBatteryService, null);
            }

            // Several instances are possible (e.g. one per earbud, or one tied to a protected HID service). The first
            // one that can be read is shown; the device is skipped only when none can be.
            int count = services.Services.Count;
            var failures = new List<BleBatteryRead>();
            for (int i = 0; i < count; i++)
            {
                var read = await ReadLevelAsync(ble, services.Services[i], ct).ConfigureAwait(false);
                if (read.Status == BleReadStatus.Ok)
                {
                    return count == 1 ? read : read with { Detail = $"Battery Service {i + 1} of {count}" };
                }

                if (read.Status == BleReadStatus.Unreachable)
                {
                    return read; // the link went: touch the device no further
                }

                failures.Add(read);
            }

            // A transient error is retried next poll; a refusal or a missing Battery Level skips the device.
            var failure = failures.FirstOrDefault(f => f.Status == BleReadStatus.ProtocolError)
                ?? failures.FirstOrDefault(f => f.Status == BleReadStatus.AccessDenied)
                ?? failures[0];
            return count == 1 ? failure : failure with { Detail = $"{failure.Detail ?? failure.Status.ToString()}; {count} Battery Services" };
        }
        finally
        {
            if (services is not null)
            {
                foreach (var service in services.Services)
                {
                    service.Dispose();
                }
            }

            ble?.Dispose();
        }
    }

    public void Dispose()
    {
        _lowEnergy.Changed -= OnLowEnergyChanged;
        _lowEnergy.Dispose();
        _classic.Dispose();
    }

    private static async Task<BleBatteryRead> ReadLevelAsync(BluetoothLEDevice ble, GattDeviceService service, CancellationToken ct)
    {
        var characteristics = await service
            .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(ct).ConfigureAwait(false);
        if (Failed(characteristics.Status, characteristics.ProtocolError) is { } characteristicFailure)
        {
            return characteristicFailure;
        }

        if (characteristics.Characteristics.Count == 0)
        {
            return new BleBatteryRead(BleReadStatus.NoBatteryService, null, "battery service has no Battery Level");
        }

        // Reading from the device starts a connection if the link has gone, so check again right before it.
        if (ble.ConnectionStatus != BluetoothConnectionStatus.Connected)
        {
            return new BleBatteryRead(BleReadStatus.Unreachable, null, "disconnected before the read");
        }

        var result = await characteristics.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct).ConfigureAwait(false);
        if (Failed(result.Status, result.ProtocolError) is { } readFailure)
        {
            return readFailure;
        }

        return result.Value is { Length: > 0 } buffer
            ? new BleBatteryRead(BleReadStatus.Ok, FirstByte(buffer))
            : new BleBatteryRead(BleReadStatus.ProtocolError, null, "empty value");
    }

    private static BleBatteryRead? Failed(GattCommunicationStatus status, byte? protocolError) => status switch
    {
        GattCommunicationStatus.Success => null,
        GattCommunicationStatus.AccessDenied => new BleBatteryRead(BleReadStatus.AccessDenied, null),
        GattCommunicationStatus.ProtocolError => new BleBatteryRead(BleReadStatus.ProtocolError, null, $"GATT error 0x{protocolError:X2}"),
        _ => new BleBatteryRead(BleReadStatus.Unreachable, null),
    };

    private static byte FirstByte(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        return reader.ReadByte();
    }

    private void OnLowEnergyChanged(object? sender, EventArgs e) => DevicesChanged?.Invoke(this, e);
}
