namespace BatteryHub.Core.Sony;

/// <summary>Values shared by the DualShock 4 and DualSense.</summary>
/// <remarks>
/// Sources: Linux drivers/hid/hid-playstation.c and drivers/hid/hid-ids.h (torvalds/linux 602042b, 2026-10-07);
/// DS4Windows (schmaldeo/ds4windows ecae27a). Offsets count from the start of the report, report ID at index 0.
/// </remarks>
internal static class SonyProtocol
{
    /// <summary>hid-ids.h USB_VENDOR_ID_SONY.</summary>
    public const ushort VendorId = 0x054C;

    /// <summary>Seed byte hashed before a Bluetooth input report (hid-playstation.c PS_INPUT_CRC32_SEED).</summary>
    public const byte InputCrcSeed = 0xA1;

    /// <summary>Seed byte hashed before a Bluetooth feature report (hid-playstation.c PS_FEATURE_CRC32_SEED).</summary>
    public const byte FeatureCrcSeed = 0xA3;

    /// <summary>Length of the little-endian CRC-32 at the end of every Bluetooth input and feature report.</summary>
    public const int CrcLength = 4;

    /// <summary>
    /// Both pads' pairing-info feature reports (DualSense 0x09, DualShock 4 USB 0x12) carry the pad's MAC at bytes 1-6,
    /// least significant byte first (hid-playstation.c dualsense_get_mac_address, dualshock4_get_mac_address).
    /// </summary>
    public const int PairingInfoMacOffset = 1;
}
