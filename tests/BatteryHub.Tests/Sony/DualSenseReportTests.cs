using BatteryHub.Core;
using BatteryHub.Core.Sony;

namespace BatteryHub.Tests.Sony;

public class DualSenseReportTests
{
    [Theory]
    [InlineData(0x00, 5, ChargeState.Discharging)]
    [InlineData(0x05, 55, ChargeState.Discharging)]
    [InlineData(0x09, 95, ChargeState.Discharging)]
    [InlineData(0x0A, 100, ChargeState.Discharging)]
    [InlineData(0x0F, 100, ChargeState.Discharging)] // levels above 10 clamp, as in hid-playstation.c
    [InlineData(0x10, 5, ChargeState.Charging)]
    [InlineData(0x18, 85, ChargeState.Charging)]
    [InlineData(0x1A, 100, ChargeState.Charging)]
    [InlineData(0x2A, 100, ChargeState.Full)]
    [InlineData(0x20, 100, ChargeState.Full)] // full is 100% whatever the level nibble says
    public void Decodes_level_and_charge_state(byte status, int percent, ChargeState charge)
    {
        var decoded = DualSenseReport.DecodeStatus(status);

        Assert.Equal(percent, decoded.Percent);
        Assert.Equal(charge, decoded.ChargeState);
        Assert.Null(decoded.Fault);
        Assert.Equal(status, decoded.StatusByte);
    }

    [Theory]
    [InlineData(0xA5, "voltage or temperature")]
    [InlineData(0xB5, "temperature error")]
    [InlineData(0xF5, "Charging error")]
    [InlineData(0x35, "Unknown charging state 0x3")]
    [InlineData(0x95, "Unknown charging state 0x9")]
    public void Reports_faults_without_inventing_a_percentage(byte status, string fault)
    {
        var decoded = DualSenseReport.DecodeStatus(status);

        Assert.Null(decoded.Percent);
        Assert.Equal(ChargeState.Unknown, decoded.ChargeState);
        Assert.Contains(fault, decoded.Fault);
    }

    [Fact]
    public void Every_status_byte_decodes_to_a_percentage_or_a_fault()
    {
        for (int value = 0; value <= 0xFF; value++)
        {
            var decoded = DualSenseReport.DecodeStatus((byte)value);
            Assert.True(decoded.Percent is >= 5 and <= 100 ^ decoded.Fault is not null, $"0x{value:X2}");
        }
    }

    [Fact]
    public void Reads_usb_status_at_byte_53()
    {
        Assert.True(DualSenseReport.TryParseUsb(SonyReports.DualSenseUsb(0x13), out var status));
        Assert.Equal(35, status.Percent);
        Assert.Equal(ChargeState.Charging, status.ChargeState);
    }

    [Fact]
    public void Reads_bluetooth_status_at_byte_54()
    {
        Assert.True(DualSenseReport.TryParseBluetooth(SonyReports.DualSenseBluetooth(0x07), out var status));
        Assert.Equal(75, status.Percent);
        Assert.Equal(ChargeState.Discharging, status.ChargeState);
    }

    [Fact]
    public void Rejects_wrong_id_short_report_or_bad_crc()
    {
        byte[] usb = SonyReports.DualSenseUsb(0x05);
        Assert.False(DualSenseReport.TryParseUsb(usb.AsSpan(0, 63), out _));
        usb[0] = 0x31;
        Assert.False(DualSenseReport.TryParseUsb(usb, out _));

        byte[] bt = SonyReports.DualSenseBluetooth(0x05);
        Assert.False(DualSenseReport.TryParseBluetooth(bt.AsSpan(0, 77), out _));
        bt[54] = 0x06; // CRC no longer matches
        Assert.False(DualSenseReport.TryParseBluetooth(bt, out _));
    }

    [Fact]
    public void Does_not_read_the_bluetooth_minimal_report()
    {
        byte[] minimal = SonyReports.Padded(SonyReports.BluetoothMinimal(), 78);

        Assert.False(DualSenseReport.TryParseBluetooth(minimal, out _));
    }
}
