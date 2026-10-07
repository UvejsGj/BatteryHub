using System.Buffers.Binary;
using System.IO.Hashing;

namespace BatteryHub.Core.Sony;

/// <summary>
/// The CRC-32 Sony pads append to Bluetooth reports: standard CRC-32 (IEEE) over a seed byte followed by
/// the report from its ID up to the CRC, stored little-endian in the report's last four bytes.
/// </summary>
/// <remarks>Source: hid-playstation.c ps_check_crc32 and the dualsense/dualshock4 BT report parsers.</remarks>
internal static class SonyCrc
{
    /// <param name="buffer">The report, report ID at index 0. May be longer than <paramref name="reportLength"/> (Windows pads reads to the longest report).</param>
    /// <param name="reportLength">The report's own length, CRC included.</param>
    public static bool IsValid(ReadOnlySpan<byte> buffer, int reportLength, byte seed)
    {
        if (reportLength <= SonyProtocol.CrcLength || buffer.Length < reportLength)
        {
            return false;
        }

        int covered = reportLength - SonyProtocol.CrcLength;
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(covered, SonyProtocol.CrcLength));
        return Compute(seed, buffer[..covered]) == stored;
    }

    public static uint Compute(byte seed, ReadOnlySpan<byte> data)
    {
        var crc = new Crc32();
        crc.Append([seed]);
        crc.Append(data);
        return crc.GetCurrentHashAsUInt32();
    }
}
