using System.Globalization;

namespace BatteryHub.Core;

/// <summary>Device ID conventions shared by providers, so the same device gets the same ID from every reader.</summary>
public static class DeviceIds
{
    /// <summary>"bt-" plus the 48-bit Bluetooth address as 12 lowercase hex digits, most significant byte first.</summary>
    public static string Bluetooth(ulong address) =>
        "bt-" + (address & 0xFFFF_FFFF_FFFF).ToString("x12", CultureInfo.InvariantCulture);
}
