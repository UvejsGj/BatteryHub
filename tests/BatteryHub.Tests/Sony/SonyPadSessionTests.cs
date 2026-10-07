using BatteryHub.Core;
using BatteryHub.Core.Sony;

namespace BatteryHub.Tests.Sony;

public class SonyPadSessionTests
{
    private static readonly SonyPadModel DualSense = SonyPadModel.Find(0x054C, 0x0CE6)!;
    private static readonly SonyPadModel DualShock4 = SonyPadModel.Find(0x054C, 0x09CC)!;
    private static readonly SonyPadModel WirelessAdapter = SonyPadModel.Find(0x054C, 0x0BA0)!;
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    private readonly FakeTime _time = new();

    [Fact]
    public void Usb_reads_the_first_report_and_requests_no_feature()
    {
        var channel = new FakeSonyChannel(_time).Input(SonyReports.DualSenseUsb(0x16));

        var result = SonyPadSession.Read(channel, DualSense, ConnectionType.Usb, true, _time, Window);

        Assert.Equal(SonyReadOutcome.Battery, result.Outcome);
        Assert.Equal(65, result.Battery?.Percent);
        Assert.Equal(ChargeState.Charging, result.Battery?.ChargeState);
        Assert.Empty(channel.FeatureRequests);
        Assert.Equal(1, result.ReportsRead);
    }

    [Fact]
    public void Bluetooth_requests_calibration_once_sized_to_the_largest_feature_report_then_skips_minimal_reports()
    {
        var channel = new FakeSonyChannel(_time) { MaxInputReportLength = 78, MaxFeatureReportLength = 64 }
            .Input(SonyReports.BluetoothMinimal(), SonyReports.BluetoothMinimal(), SonyReports.DualSenseBluetooth(0x04));

        var result = SonyPadSession.Read(channel, DualSense, ConnectionType.Bluetooth, true, _time, Window);

        Assert.Equal([(0x05, 64)], channel.FeatureRequests);
        Assert.Equal(SonyReadOutcome.Battery, result.Outcome);
        Assert.Equal(45, result.Battery?.Percent);
        Assert.Equal(3, result.ReportsRead);
        Assert.Equal(2, result.OtherReports);
        Assert.Equal(SonyReports.BluetoothMinimal(), result.FirstOtherReport);
    }

    [Fact]
    public void Bluetooth_without_permission_to_switch_requests_no_feature()
    {
        var channel = new FakeSonyChannel(_time) { MaxInputReportLength = 78 }.Input(SonyReports.BluetoothMinimal());

        var result = SonyPadSession.Read(channel, DualSense, ConnectionType.Bluetooth, false, _time, Window);

        Assert.Empty(channel.FeatureRequests);
        Assert.Equal(SonyReadOutcome.NoBatteryReport, result.Outcome);
        Assert.Equal(1, result.OtherReports);
    }

    [Fact]
    public void Bluetooth_dualshock4_uses_report_0x11_and_trims_the_windows_padding()
    {
        var channel = new FakeSonyChannel(_time) { MaxInputReportLength = 547, PadReads = true }
            .Input(SonyReports.BluetoothMinimal(), SonyReports.DualShock4Bluetooth(0x1B));

        var result = SonyPadSession.Read(channel, DualShock4, ConnectionType.Bluetooth, true, _time, Window);

        Assert.Equal(SonyReadOutcome.Battery, result.Outcome);
        Assert.Equal(ChargeState.Full, result.Battery?.ChargeState);
        Assert.Equal(78, result.BatteryReport?.Length);
        Assert.Equal(0x05, channel.FeatureRequests.Single().Id);
    }

    [Fact]
    public void Skips_reports_that_fail_their_crc()
    {
        byte[] corrupt = SonyReports.DualSenseBluetooth(0x05);
        corrupt[20] ^= 0xFF;
        var channel = new FakeSonyChannel(_time) { MaxInputReportLength = 78 }
            .Input(corrupt, SonyReports.DualSenseBluetooth(0x08));

        var result = SonyPadSession.Read(channel, DualSense, ConnectionType.Bluetooth, true, _time, Window);

        Assert.Equal(SonyReadOutcome.Battery, result.Outcome);
        Assert.Equal(85, result.Battery?.Percent);
        Assert.Equal(1, result.CrcFailures);
        Assert.Equal(corrupt, result.FirstRejectedReport);
    }

    [Fact]
    public void Keeps_reading_when_the_calibration_request_fails()
    {
        var channel = new FakeSonyChannel(_time) { MaxInputReportLength = 78, FeatureError = new IOException("GetFeature failed.") }
            .Input(SonyReports.DualSenseBluetooth(0x02));

        var result = SonyPadSession.Read(channel, DualSense, ConnectionType.Bluetooth, true, _time, Window);

        Assert.Equal("GetFeature failed.", result.CalibrationError);
        Assert.Equal(SonyReadOutcome.Battery, result.Outcome);
    }

    [Fact]
    public void Gives_up_when_the_window_passes()
    {
        var reports = Enumerable.Repeat(SonyReports.BluetoothMinimal(), 1000).ToArray();
        var channel = new FakeSonyChannel(_time) { MaxInputReportLength = 78, ReportInterval = TimeSpan.FromMilliseconds(100) }.Input(reports);

        var result = SonyPadSession.Read(channel, DualSense, ConnectionType.Bluetooth, true, _time, Window);

        Assert.Equal(SonyReadOutcome.NoBatteryReport, result.Outcome);
        Assert.Equal(5, result.ReportsRead);
        Assert.True(result.Elapsed <= Window);
    }

    [Fact]
    public void Each_read_waits_only_for_what_is_left_of_the_window()
    {
        // 300 ms between reports does not divide 500 ms: the second read must give up at the window, not wait 300 ms more.
        var channel = new FakeSonyChannel(_time) { MaxInputReportLength = 78, ReportInterval = TimeSpan.FromMilliseconds(300) }
            .Input(SonyReports.BluetoothMinimal(), SonyReports.BluetoothMinimal());

        var result = SonyPadSession.Read(channel, DualSense, ConnectionType.Bluetooth, false, _time, Window);

        Assert.Equal([Window, TimeSpan.FromMilliseconds(200)], channel.ReadTimeouts);
        Assert.Equal(1, result.ReportsRead);
        Assert.Equal(Window, result.Elapsed);
    }

    [Fact]
    public void Gives_up_when_no_report_arrives()
    {
        var channel = new FakeSonyChannel(_time);

        var result = SonyPadSession.Read(channel, DualSense, ConnectionType.Usb, true, _time, Window);

        Assert.Equal(SonyReadOutcome.NoBatteryReport, result.Outcome);
        Assert.Equal(0, result.ReportsRead);
        Assert.Equal(Window, result.Elapsed);
    }

    [Fact]
    public void Wireless_adapter_without_a_pad_is_not_read_as_five_percent()
    {
        var channel = new FakeSonyChannel(_time).Input(SonyReports.DualShock4Usb(0x00, status1: 0x04));

        var result = SonyPadSession.Read(channel, WirelessAdapter, ConnectionType.Usb, true, _time, Window);

        Assert.Equal(SonyReadOutcome.WirelessAdapterEmpty, result.Outcome);
        Assert.Null(result.Battery);
    }

    [Fact]
    public void Wireless_adapter_with_a_pad_reads_like_usb()
    {
        var channel = new FakeSonyChannel(_time).Input(SonyReports.DualShock4Usb(0x07, status1: 0x00));

        var result = SonyPadSession.Read(channel, WirelessAdapter, ConnectionType.Usb, true, _time, Window);

        Assert.Equal(75, result.Battery?.Percent);
    }
}
