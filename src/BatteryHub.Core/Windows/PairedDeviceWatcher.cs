using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BatteryHub.Core.Windows;

/// <summary>A paired Bluetooth device as Windows' association-endpoint records list it.</summary>
/// <param name="Id">Windows' endpoint ID, used to open the device.</param>
/// <param name="Name">The device's name; may be empty.</param>
/// <param name="ContainerId">Groups the endpoints and device nodes of one physical device.</param>
/// <param name="Appearance">GAP Appearance (LE only), when the device advertises one.</param>
internal sealed record PairedDevice(string Id, string Name, ulong Address, bool IsConnected, Guid? ContainerId, ushort? Appearance);

/// <summary>
/// Keeps Windows' list of paired classic or LE Bluetooth devices, and whether each is connected, current through a
/// DeviceWatcher over association endpoints, as Microsoft's BluetoothLE sample does. The paired-device selector
/// reads Windows' records: no radio scan, no connection. <see cref="Changed"/> fires when a device is added or
/// removed, connects or disconnects, and when the watcher stops.
/// </summary>
internal sealed class PairedDeviceWatcher : IDisposable
{
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string ContainerProperty = "System.Devices.Aep.ContainerId";
    private const string AppearanceProperty = "System.Devices.Aep.Bluetooth.Le.Appearance";

    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(15);

    // A watcher that stops sooner than this after starting is restarted by the next scheduled poll, not at once.
    private static readonly TimeSpan QuickStop = TimeSpan.FromSeconds(30);

    private readonly bool _lowEnergy;
    private readonly string _label;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DeviceInformation> _devices = new(StringComparer.Ordinal); // under _lock
    private TaskCompletionSource _enumerated = new(TaskCreationOptions.RunContinuationsAsynchronously); // under _lock; the current watcher's
    private DeviceWatcher? _watcher; // under _lock
    private long _watcherStartedAt; // under _lock; Environment.TickCount64
    private bool _disposed; // under _lock

    private PairedDeviceWatcher(bool lowEnergy, ILogger logger)
    {
        _lowEnergy = lowEnergy;
        _label = lowEnergy ? "Bluetooth LE" : "classic Bluetooth";
        _logger = logger;
    }

    public event EventHandler? Changed;

    public static PairedDeviceWatcher Classic(ILogger logger) => new(lowEnergy: false, logger);

    public static PairedDeviceWatcher LowEnergy(ILogger logger) => new(lowEnergy: true, logger);

    /// <summary>The paired devices now. Starts the watcher if needed and waits for its first enumeration.</summary>
    public async Task<IReadOnlyList<PairedDevice>> ListAsync(CancellationToken cancellationToken)
    {
        var enumerated = EnsureWatcher();

        // A watcher's first enumeration fills the list; later calls return immediately.
        try
        {
            await enumerated.WaitAsync(EnumerationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("Paired {Label} enumeration has not finished; using the devices found so far", _label);
        }

        lock (_lock)
        {
            return _devices.Values.Select(ToPaired).OfType<PairedDevice>().ToList();
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

    private static PairedDevice? ToPaired(DeviceInformation info)
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
        return new PairedDevice(info.Id, info.Name ?? "", address, connected, container, appearance);
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

            // IsConnected must be requested for the watcher to report connects and disconnects.
            _watcher = _lowEnergy
                ? DeviceInformation.CreateWatcher(
                    BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                    [AddressProperty, ConnectedProperty, ContainerProperty, AppearanceProperty],
                    DeviceInformationKind.AssociationEndpoint)
                : DeviceInformation.CreateWatcher(
                    BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                    [AddressProperty, ConnectedProperty, ContainerProperty],
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

    // Updates carry only the properties that changed; DeviceInformation.Update merges them.
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
    // connection state is no longer known) and listeners are told, so a poll soon after starts a new watcher. One
    // that stopped right after starting waits for the next scheduled poll instead, so a watcher that keeps failing
    // cannot cause a poll every few seconds.
    private void OnStopped(DeviceWatcher sender, object args)
    {
        bool restartSoon;
        lock (_lock)
        {
            if (sender != _watcher)
            {
                return;
            }

            _logger.LogInformation("Paired {Label} watcher stopped ({Status})", _label, sender.Status);
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
            _logger.LogDebug(ex, "Stopping the paired {Label} watcher failed", _label);
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A paired {Label} change handler threw", _label);
        }
    }
}
