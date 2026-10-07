using System.Text;
using BatteryHub.Core.Sony;

namespace BatteryHub.Tests.Sony;

public class SonyCrcTests
{
    [Fact]
    public void Is_standard_crc32_over_seed_then_data()
    {
        // CRC-32/ISO-HDLC check value: CRC of "123456789" is 0xCBF43926. Feed '1' as the seed byte.
        byte[] rest = Encoding.ASCII.GetBytes("23456789");

        Assert.Equal(0xCBF43926u, SonyCrc.Compute((byte)'1', rest));
    }

    [Fact]
    public void Validates_crc_stored_little_endian_in_last_four_bytes()
    {
        byte[] report = SonyReports.DualSenseBluetooth(0x05);

        Assert.True(SonyCrc.IsValid(report, 78, 0xA1));
    }

    [Fact]
    public void Rejects_wrong_seed_or_corrupted_byte()
    {
        byte[] report = SonyReports.DualSenseBluetooth(0x05);
        Assert.False(SonyCrc.IsValid(report, 78, 0xA3));

        report[10] ^= 0x01;
        Assert.False(SonyCrc.IsValid(report, 78, 0xA1));
    }

    [Fact]
    public void Uses_the_report_length_not_the_buffer_length()
    {
        // Windows pads every read to the longest input report (547 bytes for a Bluetooth DualShock 4).
        byte[] padded = SonyReports.Padded(SonyReports.DualShock4Bluetooth(0x05), 547);

        Assert.True(SonyCrc.IsValid(padded, 78, 0xA1));
    }

    [Fact]
    public void Rejects_buffer_shorter_than_report()
    {
        byte[] report = SonyReports.DualSenseBluetooth(0x05);

        Assert.False(SonyCrc.IsValid(report.AsSpan(0, 77), 78, 0xA1));
    }
}
