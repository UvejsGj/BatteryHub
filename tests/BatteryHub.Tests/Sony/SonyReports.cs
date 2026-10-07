using System.Buffers.Binary;
using BatteryHub.Core.Sony;

namespace BatteryHub.Tests.Sony;

/// <summary>Builds synthetic Sony reports for tests: zeros except the report ID, the status byte and the Bluetooth CRC.</summary>
internal static class SonyReports
{
    public static byte[] DualSenseUsb(byte status) => Build(0x01, 64, 53, status, crc: false);

    public static byte[] DualSenseBluetooth(byte status) => Build(0x31, 78, 54, status, crc: true);

    public static byte[] DualShock4Usb(byte status, byte status1 = 0) => Build(0x01, 64, 30, status, crc: false, (31, status1));

    public static byte[] DualShock4Bluetooth(byte status) => Build(0x11, 78, 32, status, crc: true);

    /// <summary>The 10-byte report a Bluetooth pad sends before it is switched to full reports.</summary>
    public static byte[] BluetoothMinimal() => [0x01, 0x80, 0x80, 0x80, 0x80, 0x08, 0x00, 0x00, 0x00, 0x00];

    /// <summary>A pairing-info feature report carrying <paramref name="macMostSignificantFirst"/>.</summary>
    public static byte[] PairingReport(byte id, int length, string macMostSignificantFirst, bool crc)
    {
        var report = new byte[length];
        report[0] = id;
        byte[] mac = Convert.FromHexString(macMostSignificantFirst);
        for (int i = 0; i < 6; i++)
        {
            report[1 + i] = mac[5 - i];
        }

        if (crc)
        {
            WriteCrc(report, 0xA3);
        }

        return report;
    }

    public static byte[] Padded(byte[] report, int length)
    {
        var padded = new byte[length];
        report.CopyTo(padded, 0);
        return padded;
    }

    private static byte[] Build(byte id, int length, int statusOffset, byte status, bool crc, params (int Offset, byte Value)[] extra)
    {
        var report = new byte[length];
        report[0] = id;
        report[statusOffset] = status;
        foreach (var (offset, value) in extra)
        {
            report[offset] = value;
        }

        if (crc)
        {
            WriteCrc(report, 0xA1);
        }

        return report;
    }

    private static void WriteCrc(byte[] report, byte seed)
    {
        uint crc = SonyCrc.Compute(seed, report.AsSpan(0, report.Length - 4));
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(report.Length - 4), crc);
    }
}
