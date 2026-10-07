using System.Globalization;

namespace BatteryHub.Core;

/// <summary>Parses the Bluetooth address strings Windows APIs return.</summary>
public static class BluetoothAddress
{
    /// <summary>
    /// Accepts 12 hex digits with or without ':' or '-' separators, any case
    /// (e.g. System.Devices.Aep.DeviceAddress "a0:ab:51:c0:ff:ee").
    /// </summary>
    public static bool TryParse(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string digits = text.Trim().Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return digits.Length == 12
            && digits.All(char.IsAsciiHexDigit)
            && ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
    }
}
