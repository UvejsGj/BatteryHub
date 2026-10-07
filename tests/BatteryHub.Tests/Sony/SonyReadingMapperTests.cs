using BatteryHub.Core;
using BatteryHub.Core.Sony;

namespace BatteryHub.Tests.Sony;

public class SonyReadingMapperTests
{
    private static readonly SonyPadModel DualSenseEdge = SonyPadModel.Find(0x054C, 0x0DF2)!;
    private static readonly SonyPadModel WirelessAdapter = SonyPadModel.Find(0x054C, 0x0BA0)!;
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static SonyReadResult Read(SonyReadOutcome outcome, SonyBatteryStatus? battery = null, int reports = 1, int crcFailures = 0, string? calibrationError = null) =>
        new(outcome, battery, null, null, null, null, calibrationError, reports, 0, crcFailures, TimeSpan.Zero);

    private static BatteryReading? Map(SonyReadResult read, ConnectionType connection = ConnectionType.Bluetooth, bool requestFull = true) =>
        SonyReadingMapper.FromRead(read, DualSenseEdge, connection, "sony-a0ab51c0ffee", Now, requestFull);

    [Fact]
    public void Battery_becomes_an_ok_reading()
    {
        var reading = Map(Read(SonyReadOutcome.Battery, DualSenseReport.DecodeStatus(0x18)))!;

        Assert.Equal(ReadingStatus.Ok, reading.Status);
        Assert.Equal(85, reading.Percent);
        Assert.Equal(ChargeState.Charging, reading.ChargeState);
        Assert.Null(reading.StatusDetail);
        Assert.Equal(("sony-a0ab51c0ffee", "DualSense Edge", DeviceKind.Gamepad, ConnectionType.Bluetooth, Now, "sony-hid"),
            (reading.DeviceId, reading.Name, reading.Kind, reading.Connection, reading.Timestamp, reading.Source));
    }

    [Fact]
    public void Fault_becomes_a_device_error_with_no_percentage()
    {
        var reading = Map(Read(SonyReadOutcome.Battery, DualSenseReport.DecodeStatus(0xB3)))!;

        Assert.Equal(ReadingStatus.DeviceError, reading.Status);
        Assert.Null(reading.Percent);
        Assert.Null(reading.CoarseLevel);
        Assert.Equal("Not charging: temperature error", reading.StatusDetail);
    }

    [Fact]
    public void Empty_wireless_adapter_shows_nothing()
    {
        var reading = SonyReadingMapper.FromRead(Read(SonyReadOutcome.WirelessAdapterEmpty), WirelessAdapter, ConnectionType.Usb, "id", Now, true);

        Assert.Null(reading);
    }

    [Theory]
    [InlineData(3, 1, null, true, "Bluetooth reports failed their checksum")]
    [InlineData(1, 0, "GetFeature failed.", true, "The pad refused the request for full Bluetooth reports (GetFeature failed.)")]
    [InlineData(0, 0, null, true, "The pad sent no reports")]
    [InlineData(4, 0, null, true, "The pad sent only reports without battery data")]
    [InlineData(4, 0, null, false, "The pad is sending minimal Bluetooth reports, which carry no battery data")]
    public void No_battery_report_says_why(int reports, int crcFailures, string? calibrationError, bool requestFull, string detail)
    {
        var reading = Map(Read(SonyReadOutcome.NoBatteryReport, reports: reports, crcFailures: crcFailures, calibrationError: calibrationError), requestFull: requestFull)!;

        Assert.Equal(ReadingStatus.NoData, reading.Status);
        Assert.Null(reading.Percent);
        Assert.Equal(detail, reading.StatusDetail);
    }

    [Fact]
    public void In_use_has_no_level()
    {
        var reading = SonyReadingMapper.InUse(DualSenseEdge, ConnectionType.Usb, "id", Now);

        Assert.Equal(ReadingStatus.InUseByAnotherApp, reading.Status);
        Assert.Null(reading.Percent);
        Assert.Equal(ChargeState.Unknown, reading.ChargeState);
    }
}
