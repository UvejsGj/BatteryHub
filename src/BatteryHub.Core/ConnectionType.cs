namespace BatteryHub.Core;

/// <summary>How a device reaches this PC.</summary>
public enum ConnectionType
{
    Unknown,
    Usb,
    /// <summary>Bluetooth Classic (BR/EDR).</summary>
    Bluetooth,
    /// <summary>Bluetooth Low Energy.</summary>
    Ble,
}
