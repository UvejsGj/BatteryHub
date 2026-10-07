using System.Globalization;

namespace BatteryHub.Core.Sony;

/// <summary>Turns what a pad reports about itself into a stable device ID.</summary>
/// <remarks>
/// The pad's Bluetooth MAC is the same over USB and Bluetooth, which is how hid-playstation.c recognises one pad on
/// both transports. IDs are "sony-" plus the MAC as 12 lowercase hex digits, most significant byte first.
/// </remarks>
public static class SonyIdentity
{
    private const int MacLength = 6;

    public static string FromMac(string mac) => $"sony-{mac}";

    /// <summary>Fallback when the MAC cannot be read: stable for as long as Windows keeps the same device path.</summary>
    public static string FromDevicePath(string devicePath)
    {
        // FNV-1a over the lowercased path: deterministic across runs, unlike string.GetHashCode.
        ulong hash = 14695981039346656037;
        foreach (char c in devicePath.ToLowerInvariant())
        {
            hash = (hash ^ c) * 1099511628211;
        }

        return $"sony-path-{hash:x16}";
    }

    /// <summary>
    /// Reads the MAC from a pairing-info feature report (DualSense 0x09, DualShock 4 USB 0x12): six bytes at index 1,
    /// least significant first.
    /// </summary>
    /// <param name="reportLength">The report's own length; used to find the CRC when <paramref name="checkCrc"/> is set.</param>
    /// <param name="checkCrc">True over Bluetooth, where feature reports end in a CRC-32 (seed 0xA3).</param>
    public static bool TryParsePairingReport(ReadOnlySpan<byte> buffer, byte reportId, int reportLength, bool checkCrc, out string mac)
    {
        mac = "";
        if (buffer.Length < reportLength || reportLength < SonyProtocol.PairingInfoMacOffset + MacLength || buffer[0] != reportId)
        {
            return false;
        }

        if (checkCrc && !SonyCrc.IsValid(buffer, reportLength, SonyProtocol.FeatureCrcSeed))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[MacLength];
        buffer.Slice(SonyProtocol.PairingInfoMacOffset, MacLength).CopyTo(bytes);
        bytes.Reverse();
        return TryFormat(bytes, out mac);
    }

    /// <summary>
    /// Reads a MAC from a HID serial number string such as "a0ab51c0ffee" or "A0:AB:51:C0:FF:EE".
    /// Windows reports the Bluetooth address this way for Bluetooth HID devices.
    /// </summary>
    public static bool TryParseSerial(string? serial, out string mac)
    {
        mac = "";
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        string digits = new(serial.Where(c => c is not (':' or '-' or ' ')).ToArray());
        if (digits.Length != MacLength * 2
            || !ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[MacLength];
        for (int i = 0; i < MacLength; i++)
        {
            bytes[i] = (byte)(value >> (8 * (MacLength - 1 - i)));
        }

        return TryFormat(bytes, out mac);
    }

    // All-zero and all-FF addresses are what pads and virtual devices return when they have none.
    private static bool TryFormat(ReadOnlySpan<byte> bytes, out string mac)
    {
        mac = "";
        if (!bytes.ContainsAnyExcept((byte)0x00) || !bytes.ContainsAnyExcept((byte)0xFF))
        {
            return false;
        }

        mac = Convert.ToHexStringLower(bytes);
        return true;
    }
}
