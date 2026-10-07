using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryHub.Core.Ble;

/// <summary>What one poll learned about one paired Bluetooth LE device. The Probe prints it.</summary>
/// <param name="Read">The read made this poll, or null when none was made (not connected, or skipped earlier).</param>
public sealed record BleInspection(BleDeviceInfo Device, BleBatteryRead? Read, BatteryReading? Reading, string? Note);

/// <summary>
/// Paired Bluetooth LE devices with the standard Battery Service (0x180F), read every 5 minutes and when a device
/// connects. Only devices that are already connected are read; BatteryHub never connects a device. A device that
/// refuses access or has no battery service is skipped quietly until it reconnects.
/// </summary>
/// <remarks>
/// The plan asked to subscribe to notifications where supported. That needs a write to the device's CCCD, which is a
/// persistent device setting Windows shares between every app (and uses for its own battery monitoring), so the
/// reader polls instead.
/// </remarks>
public sealed class BleBatteryProvider : IBatteryProvider
{
    public const string ProviderName = "ble-gatt";

    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromSeconds(2);

    private readonly IBleBatterySource _source;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Tracked> _tracked = new(StringComparer.Ordinal); // only touched under _gate
    private readonly ITimer _changeTimer;
    private volatile bool _disposed;

    public BleBatteryProvider(ILogger<BleBatteryProvider>? logger = null, TimeProvider? time = null)
        : this(new WinRtBleBatterySource(logger), logger, time)
    {
    }

    internal BleBatteryProvider(IBleBatterySource source, ILogger<BleBatteryProvider>? logger = null, TimeProvider? time = null)
    {
        _source = source;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _time = time ?? TimeProvider.System;
        _changeTimer = _time.CreateTimer(_ => _ = PollAfterChangeAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _source.DevicesChanged += OnDevicesChanged;
    }

    public string Name => ProviderName;

    public TimeSpan PollInterval { get; } = TimeSpan.FromMinutes(5);

    public event EventHandler<ReadingsChangedEventArgs>? ReadingsChanged;

    public async Task<IReadOnlyList<BatteryReading>> PollAsync(CancellationToken cancellationToken)
    {
        var inspections = await InspectAsync(cancellationToken).ConfigureAwait(false);
        return inspections.Select(i => i.Reading).OfType<BatteryReading>().ToList();
    }

    /// <summary>Lists paired devices and reads the connected ones. The Probe uses this too.</summary>
    public async Task<IReadOnlyList<BleInspection>> InspectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await InspectCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.DevicesChanged -= OnDevicesChanged;
        _changeTimer.Dispose();
        _source.Dispose();
    }

    private async Task<List<BleInspection>> InspectCoreAsync(CancellationToken cancellationToken)
    {
        var devices = await _source.ListPairedAsync(cancellationToken).ConfigureAwait(false);

        // A device that disconnected starts afresh next time: a skip or an old value no longer applies.
        var connected = devices.Where(d => d.IsConnected).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in _tracked.Keys.Where(id => !connected.Contains(id)).ToList())
        {
            _tracked.Remove(id);
        }

        var inspections = new List<BleInspection>();
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!device.IsConnected)
            {
                inspections.Add(new(device, null, null, "not connected; not read (BatteryHub never connects a device)"));
                continue;
            }

            if (!_tracked.TryGetValue(device.Id, out var tracked))
            {
                tracked = new Tracked();
                _tracked[device.Id] = tracked;
            }

            if (tracked.SkipReason is { } skip)
            {
                inspections.Add(new(device, null, null, skip));
                continue;
            }

            var read = await ReadSafelyAsync(device, cancellationToken).ConfigureAwait(false);
            var (reading, note) = Apply(device, tracked, read);
            inspections.Add(new(device, read, reading, note));
        }

        return inspections;
    }

    private (BatteryReading? Reading, string? Note) Apply(BleDeviceInfo device, Tracked tracked, BleBatteryRead read)
    {
        switch (read.Status)
        {
            case BleReadStatus.Ok when read.Value is { } value && BleBattery.Percent(value) is { } percent:
                return (ReadingFor(device, percent), null);
            case BleReadStatus.Ok:
                return (null, $"invalid battery level {read.Value}; not shown");
            case BleReadStatus.AccessDenied:
                tracked.SkipReason = "Windows does not let apps read this device's battery; skipped until it reconnects";
                return (null, tracked.SkipReason);
            case BleReadStatus.NoBatteryService:
                tracked.SkipReason = "no battery service; skipped until it reconnects";
                return (null, tracked.SkipReason);
            default:
                return (null, $"read failed ({read.Status}{(read.Detail is null ? "" : ", " + read.Detail)}); retried next poll");
        }
    }

    private async Task<BleBatteryRead> ReadSafelyAsync(BleDeviceInfo device, CancellationToken cancellationToken)
    {
        try
        {
            return await _source.ReadAsync(device, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // WinRT reports a device that vanished mid-read as a COMException or ObjectDisposedException, and the
            // source's own timeout as a cancellation; none of these should fail the whole poll.
            _logger.LogDebug(ex, "Reading the battery of {Name} failed", device.Name);
            return new BleBatteryRead(BleReadStatus.Unreachable, null, ex.GetType().Name);
        }
    }

    private BatteryReading ReadingFor(BleDeviceInfo device, int percent) => new()
    {
        DeviceId = DeviceIds.Bluetooth(device.Address),
        Name = device.Name,
        Kind = BleBattery.KindFromAppearance(device.Appearance),
        Connection = ConnectionType.Ble,
        Percent = percent,
        Timestamp = _time.GetUtcNow(),
        Source = ProviderName,
    };

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        try
        {
            _changeTimer.Change(ChangeDebounce, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task PollAfterChangeAsync()
    {
        if (_disposed || ReadingsChanged is null)
        {
            return;
        }

        try
        {
            var readings = await PollAsync(CancellationToken.None).ConfigureAwait(false);
            ReadingsChanged?.Invoke(this, new ReadingsChangedEventArgs(readings));
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            // Runs on a timer thread; an escaping exception would end the process.
            _logger.LogWarning(ex, "Polling Bluetooth LE devices after a change failed");
        }
    }

    private sealed class Tracked
    {
        public string? SkipReason { get; set; }
    }
}
