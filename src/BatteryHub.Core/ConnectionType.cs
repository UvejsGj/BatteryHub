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

    /// <summary>Wireless, transport not known (XInput cannot tell the Xbox Wireless Adapter from Bluetooth).</summary>
    Wireless,
}
