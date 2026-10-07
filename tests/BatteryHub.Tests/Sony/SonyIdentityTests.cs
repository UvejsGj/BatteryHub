using BatteryHub.Core.Sony;

namespace BatteryHub.Tests.Sony;

public class SonyIdentityTests
{
    [Fact]
    public void Pairing_report_stores_the_mac_least_significant_byte_first()
    {
        byte[] report = SonyReports.PairingReport(0x12, 16, "a0ab51c0ffee", crc: false);
        Assert.Equal(new byte[] { 0x12, 0xEE, 0xFF, 0xC0, 0x51, 0xAB, 0xA0 }, report[..7]);

        Assert.True(SonyIdentity.TryParsePairingReport(report, 0x12, 16, checkCrc: false, out string mac));
        Assert.Equal("a0ab51c0ffee", mac);
        Assert.Equal("sony-a0ab51c0ffee", SonyIdentity.FromMac(mac));
    }

    [Fact]
    public void Bluetooth_pairing_report_must_pass_its_crc_even_inside_a_padded_buffer()
    {
        byte[] report = SonyReports.Padded(SonyReports.PairingReport(0x09, 20, "a0ab51c0ffee", crc: true), 64);
        Assert.True(SonyIdentity.TryParsePairingReport(report, 0x09, 20, checkCrc: true, out _));

        report[3] ^= 0x01;
        Assert.False(SonyIdentity.TryParsePairingReport(report, 0x09, 20, checkCrc: true, out _));
    }

    [Fact]
    public void Rejects_another_reports_data_and_blank_addresses()
    {
        // Another app's concurrent feature request can hand back a different report.
        byte[] other = SonyReports.PairingReport(0x05, 20, "a0ab51c0ffee", crc: false);
        Assert.False(SonyIdentity.TryParsePairingReport(other, 0x09, 20, checkCrc: false, out _));

        Assert.False(SonyIdentity.TryParsePairingReport(SonyReports.PairingReport(0x12, 16, "000000000000", crc: false), 0x12, 16, false, out _));
        Assert.False(SonyIdentity.TryParsePairingReport(SonyReports.PairingReport(0x12, 16, "ffffffffffff", crc: false), 0x12, 16, false, out _));
    }

    [Theory]
    [InlineData("a0ab51c0ffee", "a0ab51c0ffee")]
    [InlineData("A0AB51C0FFEE", "a0ab51c0ffee")]
    [InlineData("A0:AB:51:C0:FF:EE", "a0ab51c0ffee")]
    [InlineData("a0-ab-51-c0-ff-ee", "a0ab51c0ffee")]
    public void Reads_mac_from_serial_string_in_written_order(string serial, string expected)
    {
        Assert.True(SonyIdentity.TryParseSerial(serial, out string mac));
        Assert.Equal(expected, mac);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("000000000000")]
    [InlineData("a0ab51c0ff")]
    [InlineData("not-a-mac-at-all")]
    public void Rejects_serials_that_are_not_a_mac(string? serial)
    {
        Assert.False(SonyIdentity.TryParseSerial(serial, out _));
    }

    [Fact]
    public void Path_fallback_is_stable_and_ignores_case()
    {
        string a = SonyIdentity.FromDevicePath(@"\\?\hid#vid_054c&pid_0ce6&mi_03#8&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        string b = SonyIdentity.FromDevicePath(@"\\?\HID#VID_054C&PID_0CE6&MI_03#8&2A3B4C5D&0&0000#{4D1E55B2-F16F-11CF-88CB-001111000030}");

        Assert.Equal(a, b);
        Assert.Equal("sony-path-d9ee2aaa24255e02", a); // FNV-1a 64, checked with an independent implementation
    }
}
