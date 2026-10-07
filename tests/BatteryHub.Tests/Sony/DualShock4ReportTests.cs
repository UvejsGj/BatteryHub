using BatteryHub.Core;
using BatteryHub.Core.Sony;

namespace BatteryHub.Tests.Sony;

public class DualShock4ReportTests
{
    [Theory]
    [InlineData(0x00, 5, ChargeState.Discharging)]
    [InlineData(0x06, 65, ChargeState.Discharging)]
    [InlineData(0x09, 95, ChargeState.Discharging)]
    [InlineData(0x0A, 100, ChargeState.Discharging)]
    [InlineData(0x0B, 100, ChargeState.Discharging)] // without the cable every level from 10 up reads 100%
    [InlineData(0x0F, 100, ChargeState.Discharging)]
    [InlineData(0x10, 5, ChargeState.Charging)]
    [InlineData(0x19, 95, ChargeState.Charging)]
    [InlineData(0x1A, 100, ChargeState.Charging)]
    [InlineData(0x1B, 100, ChargeState.Full)]
    [InlineData(0xE6, 65, ChargeState.Discharging)] // bits 5-7 are not used
    [InlineData(0xFB, 100, ChargeState.Full)]
    public void Decodes_level_and_cable(byte status, int percent, ChargeState charge)
    {
        var decoded = DualShock4Report.DecodeStatus(status);

        Assert.Equal(percent, decoded.Percent);
        Assert.Equal(charge, decoded.ChargeState);
        Assert.Null(decoded.Fault);
    }

    [Theory]
    [InlineData(0x1C, "Unknown charging state (level 12 with cable)")]
    [InlineData(0x1D, "Unknown charging state (level 13 with cable)")]
    [InlineData(0x1E, "voltage or temperature")]
    [InlineData(0x1F, "Charging error")]
    public void Reports_faults_with_cable_without_inventing_a_percentage(byte status, string fault)
    {
        var decoded = DualShock4Report.DecodeStatus(status);

        Assert.Null(decoded.Percent);
        Assert.Equal(ChargeState.Unknown, decoded.ChargeState);
        Assert.Contains(fault, decoded.Fault);
    }

    [Fact]
    public void Reads_usb_status_at_byte_30()
    {
        Assert.True(DualShock4Report.TryParseUsb(SonyReports.DualShock4Usb(0x14), out var status));
        Assert.Equal(45, status.Percent);
        Assert.Equal(ChargeState.Charging, status.ChargeState);
    }

    [Fact]
    public void Reads_bluetooth_status_at_byte_32_from_a_padded_windows_buffer()
    {
        byte[] padded = SonyReports.Padded(SonyReports.DualShock4Bluetooth(0x03), 547);

        Assert.True(DualShock4Report.TryParseBluetooth(padded, out var status));
        Assert.Equal(35, status.Percent);
    }

    [Fact]
    public void Rejects_wrong_id_short_report_or_bad_crc()
    {
        Assert.False(DualShock4Report.TryParseUsb(SonyReports.DualShock4Usb(0x05).AsSpan(0, 31), out _));
        Assert.False(DualShock4Report.TryParseBluetooth(SonyReports.DualShock4Usb(0x05), out _));

        byte[] bt = SonyReports.DualShock4Bluetooth(0x05);
        bt[1] = 0xC0;
        Assert.False(DualShock4Report.TryParseBluetooth(bt, out _));
    }

    [Fact]
    public void Detects_wireless_adapter_with_no_pad()
    {
        Assert.True(DualShock4Report.IsWirelessAdapterEmpty(SonyReports.DualShock4Usb(0x00, status1: 0x04)));
        Assert.False(DualShock4Report.IsWirelessAdapterEmpty(SonyReports.DualShock4Usb(0x08, status1: 0x00)));
        Assert.False(DualShock4Report.IsWirelessAdapterEmpty(SonyReports.DualShock4Usb(0x08, status1: 0x03)));
    }
}
