using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BatteryHub.Core.Ble;

/// <summary>Paired Bluetooth LE devices and their Battery Level, through WinRT.</summary>
/// <remarks>
/// Based on Microsoft's GATT client guidance and the BluetoothLE sample (microsoft/Windows-universal-samples):
/// one DeviceWatcher over paired-device association endpoints keeps the device list and connection state current
/// (FindAllAsync on endpoints can take 10 s or more); a read re-checks the connection first, looks the service and
/// characteristic up in Windows' cache (an uncached lookup would start a connection), reads the value from the
/// device, and disposes every WinRT object so BatteryHub never keeps a link alive.
/// </remarks>
internal sealed class WinRtBleBatterySource : IBleBatterySource
{
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string AppearanceProperty = "System.Devices.Aep.Bluetooth.Le.Appearance";

    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(15);

    // An unreachable device times out after about 7 s per queued request.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DeviceInformation> _devices = new(StringComparer.Ordinal); // under _lock
    private readonly TaskCompletionSource _enumerated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DeviceWatcher? _watcher; // under _lock
    private bool _disposed; // under _lock

    public WinRtBleBatterySource(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public event EventHandler? DevicesChanged;

    public async Task<IReadOnlyList<BleDeviceInfo>> ListPairedAsync(CancellationToken cancellationToken)
    {
        EnsureWatcher();

        // The first enumeration fills the list; later calls return immediately.
        try
        {
            await _enumerated.Task.WaitAsync(EnumerationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("Bluetooth LE enumeration has not finished; using the devices found so far");
        }

        lock (_lock)
        {
            return _devices.Values.Select(ToInfo).OfType<BleDeviceInfo>().ToList();
        }
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

            // Several Battery Service instances (e.g. per earbud) are possible; the first one is read.
            var characteristics = await services.Services[0]
                .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(ct).ConfigureAwait(false);
            if (Failed(characteristics.Status, characteristics.ProtocolError) is { } characteristicFailure)
            {
                return characteristicFailure;
            }

            if (characteristics.Characteristics.Count == 0)
            {
                return new BleBatteryRead(BleReadStatus.NoBatteryService, null, "battery service has no Battery Level");
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
        DeviceWatcher? watcher;
        lock (_lock)
        {
            _disposed = true;
            watcher = _watcher;
            _watcher = null;
        }

        if (watcher is not null)
        {
            Unsubscribe(watcher);
            StopQuietly(watcher);
        }
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

    private static BleDeviceInfo? ToInfo(DeviceInformation info)
    {
        if (!info.Properties.TryGetValue(AddressProperty, out object? addressValue)
            || !BluetoothAddress.TryParse(addressValue as string, out ulong address))
        {
            return null;
        }

        bool connected = info.Properties.TryGetValue(ConnectedProperty, out object? connectedValue) && connectedValue is true;
        ushort? appearance = info.Properties.TryGetValue(AppearanceProperty, out object? appearanceValue) && appearanceValue is ushort raw && raw != 0
            ? raw
            : null;
        string name = string.IsNullOrWhiteSpace(info.Name) ? DeviceIds.Bluetooth(address) : info.Name;
        return new BleDeviceInfo(info.Id, name, address, connected, appearance);
    }

    private void EnsureWatcher()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watcher is not null)
            {
                return;
            }

            // The paired selector asks for no radio scan.
            _watcher = DeviceInformation.CreateWatcher(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                [AddressProperty, ConnectedProperty, AppearanceProperty],
                DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += OnAdded;
            _watcher.Updated += OnUpdated;
            _watcher.Removed += OnRemoved;
            _watcher.EnumerationCompleted += OnEnumerationCompleted;
            _watcher.Stopped += OnStopped;
            _watcher.Start();
        }
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        lock (_lock)
        {
            if (sender != _watcher)
            {
                return;
            }

            _devices[info.Id] = info;
        }

        RaiseChanged();
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_lock)
        {
            if (sender != _watcher || !_devices.TryGetValue(update.Id, out var info))
            {
                return;
            }

            info.Update(update);
        }

        RaiseChanged();
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_lock)
        {
            if (sender != _watcher || !_devices.Remove(update.Id))
            {
                return;
            }
        }

        RaiseChanged();
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args) => _enumerated.TrySetResult();

    // A watcher that aborts (e.g. the Bluetooth service restarted) raises nothing more; start a new one next poll.
    private void OnStopped(DeviceWatcher sender, object args)
    {
        lock (_lock)
        {
            if (sender != _watcher)
            {
                return;
            }

            _logger.LogInformation("Bluetooth LE watcher stopped ({Status}); restarting on the next poll", sender.Status);
            Unsubscribe(sender);
            _watcher = null;
            _devices.Clear();
        }
    }

    private void Unsubscribe(DeviceWatcher watcher)
    {
        watcher.Added -= OnAdded;
        watcher.Updated -= OnUpdated;
        watcher.Removed -= OnRemoved;
        watcher.EnumerationCompleted -= OnEnumerationCompleted;
        watcher.Stopped -= OnStopped;
    }

    private void StopQuietly(DeviceWatcher watcher)
    {
        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Stopping the Bluetooth LE watcher failed");
        }
    }

    private void RaiseChanged()
    {
        try
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A DevicesChanged handler threw");
        }
    }
}
