namespace BatteryHub.Core.Ble;

/// <summary>A paired Bluetooth LE device as Windows lists it.</summary>
/// <param name="Id">Windows' device ID, used to open it.</param>
/// <param name="Appearance">GAP Appearance value (category in the top 10 bits), when the device advertises one.</param>
public sealed record BleDeviceInfo(string Id, string Name, ulong Address, bool IsConnected, ushort? Appearance);

public enum BleReadStatus
{
    Ok,

    /// <summary>The device has no Battery Service, or no Battery Level characteristic in it.</summary>
    NoBatteryService,

    /// <summary>Windows or the device refused access (common for keyboards, mice and pads Windows itself owns).</summary>
    AccessDenied,

    /// <summary>The device stopped answering, usually because it disconnected.</summary>
    Unreachable,

    /// <summary>The device answered with an error.</summary>
    ProtocolError,
}

/// <summary>Result of reading Battery Level (0x2A19) once.</summary>
/// <param name="Value">The raw byte; only meaningful when <paramref name="Status"/> is Ok.</param>
/// <param name="Detail">Extra information for the Probe (e.g. the GATT protocol error).</param>
public sealed record BleBatteryRead(BleReadStatus Status, byte? Value, string? Detail = null);

/// <summary>The WinRT calls the Bluetooth LE reader needs, abstracted so its logic can be tested.</summary>
internal interface IBleBatterySource : IDisposable
{
    /// <summary>Raised when a paired device connects or disconnects.</summary>
    event EventHandler? DevicesChanged;

    Task<IReadOnlyList<BleDeviceInfo>> ListPairedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads Battery Level from the device itself (not Windows' value cache). Only call it for connected devices:
    /// any read contacts the device.
    /// </summary>
    Task<BleBatteryRead> ReadAsync(BleDeviceInfo device, CancellationToken cancellationToken);
}
