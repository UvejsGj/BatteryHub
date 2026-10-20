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
    private const string ContainerProperty = "System.Devices.Aep.ContainerId";

    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(15);

    // Paired-device queries read Windows' records and answer in milliseconds.
    private static readonly TimeSpan ClassicQueryTimeout = TimeSpan.FromSeconds(10);

    // An unreachable device times out after about 7 s per queued request.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    // A watcher that stops sooner than this after starting is restarted by the next scheduled poll, not at once.
    private static readonly TimeSpan QuickStop = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DeviceInformation> _devices = new(StringComparer.Ordinal); // under _lock
    private TaskCompletionSource _enumerated = new(TaskCreationOptions.RunContinuationsAsynchronously); // under _lock; the current watcher's
    private DeviceWatcher? _watcher; // under _lock
    private long _watcherStartedAt; // under _lock; Environment.TickCount64
    private bool _disposed; // under _lock

    public WinRtBleBatterySource(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public event EventHandler? DevicesChanged;

    public async Task<IReadOnlyList<BleDeviceInfo>> ListPairedAsync(CancellationToken cancellationToken)
    {
        var enumerated = EnsureWatcher();

        // A watcher's first enumeration fills the list; later calls return immediately.
        try
        {
            await enumerated.WaitAsync(EnumerationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("Bluetooth LE enumeration has not finished; using the devices found so far");
        }

        var classic = await ListClassicAsync(cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            return _devices.Values.Select(info => ToInfo(info, classic)).OfType<BleDeviceInfo>().ToList();
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

    // The classic halves of paired dual-mode devices, so their LE readings can be keyed by the classic address.
    // Without them (query failed) LE readings are keyed by the LE address, which is right for LE-only devices.
    private async Task<List<(ulong Address, Guid? Container)>> ListClassicAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ClassicQueryTimeout);
        try
        {
            var found = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                [AddressProperty, ContainerProperty],
                DeviceInformationKind.AssociationEndpoint).AsTask(timeout.Token).ConfigureAwait(false);
            var classic = new List<(ulong, Guid?)>();
            foreach (var info in found)
            {
                if (info.Properties.TryGetValue(AddressProperty, out object? value) && BluetoothAddress.TryParse(value as string, out ulong address))
                {
                    classic.Add((address, ContainerOf(info)));
                }
            }

            return classic;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Best effort: a timeout or a WinRT error here only loses the dual-mode join.
            _logger.LogDebug(ex, "Listing paired classic Bluetooth devices failed");
            return [];
        }
    }

    private static Guid? ContainerOf(DeviceInformation info) =>
        info.Properties.TryGetValue(ContainerProperty, out object? value) && value is Guid id ? id : null;

    private static BleDeviceInfo? ToInfo(DeviceInformation info, List<(ulong Address, Guid? Container)> classic)
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
        ulong? classicAddress = BluetoothIdentity.ClassicAddressInContainer(ContainerOf(info), classic);
        return new BleDeviceInfo(info.Id, name, address, connected, appearance, classicAddress == address ? null : classicAddress);
    }

    /// <summary>Starts a watcher if none is running; returns its first enumeration.</summary>
    private Task EnsureWatcher()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watcher is not null)
            {
                return _enumerated.Task;
            }

            _enumerated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _watcherStartedAt = Environment.TickCount64;

            // The paired selector asks for no radio scan.
            _watcher = DeviceInformation.CreateWatcher(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                [AddressProperty, ConnectedProperty, AppearanceProperty, ContainerProperty],
                DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += OnAdded;
            _watcher.Updated += OnUpdated;
            _watcher.Removed += OnRemoved;
            _watcher.EnumerationCompleted += OnEnumerationCompleted;
            _watcher.Stopped += OnStopped;
            _watcher.Start();
            return _enumerated.Task;
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

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        lock (_lock)
        {
            if (sender == _watcher)
            {
                _enumerated.TrySetResult();
            }
        }
    }

    // A watcher that aborts (e.g. the Bluetooth service restarted) raises nothing more. Its devices are dropped (their
    // connection state is no longer known) and a poll soon after starts a new watcher. One that stopped right after
    // starting waits for the next scheduled poll instead, so a watcher that keeps failing cannot cause a poll every
    // few seconds.
    private void OnStopped(DeviceWatcher sender, object args)
    {
        bool restartSoon;
        lock (_lock)
        {
            if (sender != _watcher)
            {
                return;
            }

            _logger.LogInformation("Bluetooth LE watcher stopped ({Status})", sender.Status);
            Unsubscribe(sender);
            _watcher = null;
            _devices.Clear();
            _enumerated.TrySetResult(); // release a poll waiting for this watcher's enumeration
            restartSoon = Environment.TickCount64 - _watcherStartedAt >= (long)QuickStop.TotalMilliseconds;
        }

        if (restartSoon)
        {
            RaiseChanged();
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
