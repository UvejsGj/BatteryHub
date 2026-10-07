namespace BatteryHub.Core.Hid;

/// <summary>Reads what a Windows HID device interface path says about the device.</summary>
public static class HidDevicePath
{
    // Bluetooth service class UUIDs that the Windows Bluetooth HID stacks put in the HID instance ID.
    // Source: Bluetooth SIG Assigned Numbers (0x1124 Human Interface Device, 0x1812 HID over GATT).
    private const string BluetoothHidService = "{00001124-0000-1000-8000-00805f9b34fb}";
    private const string HidOverGattService = "{00001812-0000-1000-8000-00805f9b34fb}";

    // USB HID instance IDs start with the USB hardware ID, e.g. HID\VID_054C&PID_0CE6&MI_03.
    private const string UsbHidPrefix = @"\\?\hid#vid_";

    /// <summary>
    /// Guesses the connection from the path alone. Returns <see cref="ConnectionType.Unknown"/> for
    /// anything it does not recognise (I2C, virtual and vendor bus devices, non-Windows paths).
    /// </summary>
    public static ConnectionType GuessConnection(string devicePath)
    {
        if (devicePath.Contains(BluetoothHidService, StringComparison.OrdinalIgnoreCase))
        {
            return ConnectionType.Bluetooth;
        }

        if (devicePath.Contains(HidOverGattService, StringComparison.OrdinalIgnoreCase))
        {
            return ConnectionType.Ble;
        }

        if (devicePath.StartsWith(UsbHidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ConnectionType.Usb;
        }

        return ConnectionType.Unknown;
    }
}
