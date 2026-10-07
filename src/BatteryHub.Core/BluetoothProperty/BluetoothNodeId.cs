using System.Globalization;
using BatteryHub.Core.Windows;

namespace BatteryHub.Core.BluetoothProperty;

/// <summary>Reads Bluetooth device instance IDs and battery values. Pure, so it can be tested off Windows.</summary>
internal static class BluetoothNodeId
{
    /// <summary>The node kinds the reader uses; null for every other Bluetooth node.</summary>
    public static BluetoothNodeKind? Classify(string instanceId)
    {
        if (instanceId.StartsWith(BluetoothPropertyConstants.HandsFreePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BluetoothNodeKind.HandsFree;
        }

        if (instanceId.StartsWith(BluetoothPropertyConstants.LowEnergyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BluetoothNodeKind.LowEnergy;
        }

        return null;
    }

    /// <summary>
    /// The remote device's address in an instance ID:
    /// classic services "BTHENUM\{uuid}_...\8&amp;2A1C3E4F&amp;0&amp;A0AB51C0FFEE_C00000000",
    /// LE devices "BTHLE\DEV_A0AB51C0FFEE\7&amp;...&amp;0&amp;a0ab51c0ffee".
    /// False for the local radio's own services (address 000000000000) and anything else.
    /// </summary>
    public static bool TryGetAddress(string instanceId, out ulong address)
    {
        address = 0;
        string[] parts = instanceId.Split('\\');
        if (parts.Length != 3)
        {
            return false;
        }

        string? digits = null;
        if (parts[0].Equals("BTHLE", StringComparison.OrdinalIgnoreCase)
            && parts[1].StartsWith("DEV_", StringComparison.OrdinalIgnoreCase))
        {
            digits = parts[1][4..];
        }
        else if (parts[0].Equals("BTHENUM", StringComparison.OrdinalIgnoreCase)
            && parts[2].EndsWith(BluetoothPropertyConstants.ClassicServiceSuffix, StringComparison.OrdinalIgnoreCase))
        {
            string last = parts[2][..^BluetoothPropertyConstants.ClassicServiceSuffix.Length];
            digits = last[(last.LastIndexOf('&') + 1)..];
        }

        return TryParseAddress(digits, out address);
    }

    /// <summary>12 hex digits, not all zero.</summary>
    public static bool TryParseAddress(string? digits, out ulong address)
    {
        address = 0;
        return digits is { Length: 12 }
            && digits.All(char.IsAsciiHexDigit)
            && ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address)
            && address != 0;
    }

    /// <summary>
    /// The stored battery level, only when it is exactly one DEVPROP_TYPE_BYTE of 0-100. Anything else (unset, another
    /// type or width, over 100) means Windows has no usable value; it is never clamped or converted.
    /// </summary>
    public static int? Percent(DeviceProperty battery) =>
        battery is { Found: true, Type: DeviceProperty.TypeByte, Data: [var value] } && value <= 100 ? value : null;

    /// <summary>The device's name from a hands-free node's friendly name ("WH-1000XM4 Hands-Free AG").</summary>
    public static string? NameFromFriendlyName(string? friendlyName)
    {
        const string suffix = " Hands-Free AG";
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return null;
        }

        string name = friendlyName.Trim();
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
    }
}
