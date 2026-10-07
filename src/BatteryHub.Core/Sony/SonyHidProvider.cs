using BatteryHub.Core.Hid;
using BatteryHub.Core.Windows;
using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryHub.Core.Sony;

/// <summary>What one poll learned about one Sony HID device. The Probe prints all of it.</summary>
/// <param name="IsVirtual">A software-created pad (e.g. DS4Windows' ViGEm DualShock 4). Never opened, never shown.</param>
/// <param name="Reading">Null when there is nothing to show: a virtual pad, an empty wireless adapter, or a device that failed mid-read.</param>
/// <param name="Read">Null when the device was not read: virtual, could not be opened, or failed mid-read.</param>
/// <param name="Error">Why the open or read failed.</param>
public sealed record SonyDeviceInspection(
    SonyPadModel Model,
    string DevicePath,
    ConnectionType Connection,
    bool IsVirtual,
    string DeviceId,
    string IdSource,
    BatteryReading? Reading,
    SonyReadResult? Read,
    string? Error);

public sealed record SonyHidOptions
{
    /// <summary>
    /// Over Bluetooth, read the calibration feature report so the pad switches from its minimal report (no battery
    /// data) to the full one. The pad stays switched until it disconnects; apps that only understand the minimal
    /// report, such as DirectInput games without Steam Input, may stop seeing input from it until then.
    /// </summary>
    public bool RequestFullBluetoothReports { get; init; } = true;
}

/// <summary>
/// Reads DualShock 4 and DualSense batteries over HID. Each poll opens every pad without exclusive access, reads until a
/// battery report arrives or 500 ms pass, and closes it again. Never writes output reports.
/// </summary>
public sealed class SonyHidProvider : IBatteryProvider
{
    public const string ProviderName = "sony-hid";

    // DeviceList.Changed fires several times while one pad connects; poll once things settle.
    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromSeconds(1);

    private readonly SonyHidOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITimer _changeTimer;

    private readonly SonyIdResolver _ids = new();
    private int _deviceListGeneration;
    private volatile bool _disposed;

    public SonyHidProvider(SonyHidOptions? options = null, ILogger<SonyHidProvider>? logger = null, TimeProvider? time = null)
    {
        _options = options ?? new SonyHidOptions();
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _time = time ?? TimeProvider.System;
        _changeTimer = _time.CreateTimer(_ => _ = PollAfterChangeAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        DeviceList.Local.Changed += OnDeviceListChanged;
    }

    public string Name => ProviderName;

    public TimeSpan PollInterval { get; } = TimeSpan.FromSeconds(30);

    public event EventHandler<ReadingsChangedEventArgs>? ReadingsChanged;

    public async Task<IReadOnlyList<BatteryReading>> PollAsync(CancellationToken cancellationToken)
    {
        var inspections = await InspectAsync(cancellationToken).ConfigureAwait(false);
        return inspections.Select(i => i.Reading).OfType<BatteryReading>().ToList();
    }

    /// <summary>Reads every connected Sony pad once. <see cref="PollAsync"/> and the Probe both use this.</summary>
    public async Task<IReadOnlyList<SonyDeviceInspection>> InspectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => InspectAll(cancellationToken), cancellationToken).ConfigureAwait(false);
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
        DeviceList.Local.Changed -= OnDeviceListChanged;
        _changeTimer.Dispose();
    }

    private List<SonyDeviceInspection> InspectAll(CancellationToken cancellationToken)
    {
        _ids.BeginPoll(Volatile.Read(ref _deviceListGeneration));
        var results = new List<SonyDeviceInspection>();
        foreach (var device in DeviceList.Local.GetHidDevices(SonyProtocol.VendorId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = SonyPadModel.Find(device.VendorID, device.ProductID);
            if (model is null)
            {
                continue;
            }

            try
            {
                results.Add(Inspect(device, model));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Usually the pad disconnected mid-read. Keep going with the other pads.
                _logger.LogWarning(ex, "Reading {Name} at {Path} failed", model.Name, device.DevicePath);
                var connection = DetectConnection(device, model);
                var (id, source) = _ids.ResolveWithoutOpening(device.DevicePath, model, connection, ReadSerial(device));
                results.Add(new(model, device.DevicePath, connection, false, id, source, null, null, ex.Message));
            }
        }

        return results;
    }

    private SonyDeviceInspection Inspect(HidDevice device, SonyPadModel model)
    {
        string path = device.DevicePath;
        var connection = DetectConnection(device, model);

        if (IsVirtual(path))
        {
            _logger.LogDebug("Skipping virtual {Name} at {Path}", model.Name, path);
            return new(model, path, connection, true, "", "", null, null, null);
        }

        if (!device.TryOpen(null, out DeviceStream? opened, out Exception? openError) || opened is not HidStream stream)
        {
            opened?.Dispose();
            _logger.LogInformation(openError, "{Name} at {Path} is in use by another app", model.Name, path);
            var (id, source) = _ids.ResolveWithoutOpening(path, model, connection, ReadSerial(device));
            var reading = SonyReadingMapper.InUse(model, connection, id, _time.GetUtcNow());
            return new(model, path, connection, false, id, source, reading, null, openError?.Message);
        }

        using (stream)
        {
            var channel = new HidSharpChannel(device, stream);
            var (id, source) = _ids.Resolve(path, model, connection, ReadSerial(device), channel, _options.RequestFullBluetoothReports);
            var read = SonyPadSession.Read(channel, model, connection, _options.RequestFullBluetoothReports, _time, SonyPadSession.DefaultReadWindow);
            var reading = SonyReadingMapper.FromRead(read, model, connection, id, _time.GetUtcNow(), _options.RequestFullBluetoothReports);
            return new(model, path, connection, false, id, source, reading, read, null);
        }
    }

    // Unreadable device tree (or not Windows): treat as real rather than hide a pad.
    private bool IsVirtual(string path)
    {
        try
        {
            return VirtualDeviceFilter.IsVirtual(DeviceTree.GetAncestry(path));
        }
        catch (Exception ex) when (ex is IOException or DllNotFoundException or EntryPointNotFoundException)
        {
            _logger.LogDebug(ex, "Could not read the device tree for {Path}", path);
            return false;
        }
    }

    private static ConnectionType DetectConnection(HidDevice device, SonyPadModel model)
    {
        if (model.IsWirelessAdapter)
        {
            return ConnectionType.Usb;
        }

        var fromPath = HidDevicePath.GuessConnection(device.DevicePath);
        if (fromPath is ConnectionType.Usb or ConnectionType.Bluetooth)
        {
            return fromPath;
        }

        // Unrecognised path: USB pads declare 64-byte input reports, Bluetooth ones longer (DS4Windows' test).
        try
        {
            return device.GetMaxInputReportLength() == model.BatteryReportLength(ConnectionType.Usb)
                ? ConnectionType.Usb
                : ConnectionType.Bluetooth;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return ConnectionType.Usb;
        }
    }

    private static string? ReadSerial(HidDevice device)
    {
        try
        {
            return device.GetSerialNumber();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return null;
        }
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        Interlocked.Increment(ref _deviceListGeneration);
        try
        {
            _changeTimer.Change(ChangeDebounce, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Disposed between HidSharp raising the event and this handler running.
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
            _logger.LogWarning(ex, "Polling after a device change failed");
        }
    }
}
