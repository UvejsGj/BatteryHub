using System.Globalization;

namespace BatteryHub.Core;

/// <summary>Parses the Bluetooth address strings Windows APIs return.</summary>
public static class BluetoothAddress
{
    private const ulong Broadcast = 0xFFFF_FFFF_FFFF;

    /// <summary>
    /// Accepts 12 hex digits with or without ':' or '-' separators, any case
    /// (e.g. System.Devices.Aep.DeviceAddress "a0:ab:51:c0:ff:ee", DEVPKEY_Bluetooth_DeviceAddress "A0AB51C0FFEE").
    /// Rejects 00:00:00:00:00:00 (the PC's own services) and ff:ff:ff:ff:ff:ff, which name no device.
    /// </summary>
    public static bool TryParse(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string digits = text.Trim().Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (digits.Length != 12
            || !digits.All(char.IsAsciiHexDigit)
            || !ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong parsed)
            || parsed is 0 or Broadcast)
        {
            return false;
        }

        address = parsed;
        return true;
    }
}
