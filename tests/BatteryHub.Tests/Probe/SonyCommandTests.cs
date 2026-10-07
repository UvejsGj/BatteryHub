using BatteryHub.Core;
using BatteryHub.Core.Sony;
using BatteryHub.Probe;
using BatteryHub.Tests.Fixtures;
using BatteryHub.Tests.Sony;

namespace BatteryHub.Tests.Probe;

public class SonyCommandTests
{
    private static readonly SonyPadModel DualSense = SonyPadModel.Find(0x054C, 0x0CE6)!;

    private static string Print(SonyDeviceInspection pad)
    {
        var output = new StringWriter();
        SonyCommand.Print(output, 1, pad);
        return output.ToString();
    }

    private static BatteryReading Reading(int? percent, ChargeState charge, ReadingStatus status = ReadingStatus.Ok, string? detail = null) => new()
    {
        DeviceId = "sony-a0ab51c0ffee",
        Name = "DualSense",
        Kind = DeviceKind.Gamepad,
        Connection = ConnectionType.Bluetooth,
        Timestamp = DateTimeOffset.UnixEpoch,
        Source = SonyHidProvider.ProviderName,
        Percent = percent,
        ChargeState = charge,
        Status = status,
        StatusDetail = detail,
    };

    [Fact]
    public void Prints_a_fixture_block_that_parses_back_to_the_same_reading()
    {
        byte[] report = SonyReports.DualSenseBluetooth(0x18);
        DualSenseReport.TryParseBluetooth(report, out var status);
        var read = new SonyReadResult(SonyReadOutcome.Battery, status, report, null, SonyReports.Padded(SonyReports.BluetoothMinimal(), 78),
            new byte[64], null, 3, 2, 0, TimeSpan.FromMilliseconds(12));
        var pad = new SonyDeviceInspection(DualSense, @"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#x", ConnectionType.Bluetooth,
            false, "sony-a0ab51c0ffee", "serial number", Reading(85, ChargeState.Charging), read, null);

        string text = Print(pad);

        Assert.Contains("battery byte : [54] = 0x18 -> level 8, state 0x1", text);
        Assert.Contains("reading      : 85% Charging", text);
        Assert.Contains("first other  : 01 80 80 80 80 08 (+72 zero bytes)", text);
        Assert.Contains("3 read in 12 ms, 2 without battery data, 0 failed CRC", text);

        string block = string.Join('\n', text.Split('\n')
            .SkipWhile(l => !l.Contains("fixture      :"))
            .Skip(1)
            .TakeWhile(l => !l.Contains("path         :")));
        var fixture = Fixture.Parse("printed", block);
        Assert.Equal(report, fixture.Bytes);
        Assert.Equal("85% Charging", fixture["expect"]);
        Assert.Equal("0CE6", fixture["product-id"]);
        Assert.Equal("Bluetooth", fixture["connection"]);
    }

    [Fact]
    public void Fault_fixture_block_says_fault_and_passes_the_fixture_check()
    {
        byte[] report = SonyReports.DualSenseBluetooth(0xA4);
        DualSenseReport.TryParseBluetooth(report, out var status);
        var read = new SonyReadResult(SonyReadOutcome.Battery, status, report, null, null, new byte[64], null, 1, 0, 0, TimeSpan.Zero);
        var reading = SonyReadingMapper.FromRead(read, DualSense, ConnectionType.Bluetooth, "id", DateTimeOffset.UnixEpoch, true)!;
        var pad = new SonyDeviceInspection(DualSense, "path", ConnectionType.Bluetooth, false, "id", "serial number", reading, read, null);

        string text = Print(pad);

        Assert.Contains("reading      : DeviceError: Not charging: voltage or temperature out of range", text);
        string block = string.Join('\n', text.Split('\n')
            .SkipWhile(l => !l.Contains("fixture      :"))
            .Skip(1)
            .TakeWhile(l => !l.Contains("path         :")));
        var fixture = Fixture.Parse("printed", block);
        Assert.Equal("fault", fixture["expect"]);
        Assert.True(DualSenseReport.TryParseBluetooth(fixture.Bytes, out var reparsed));
        Assert.NotNull(reparsed.Fault);
    }

    [Fact]
    public void Prints_why_there_is_no_reading()
    {
        var read = new SonyReadResult(SonyReadOutcome.NoBatteryReport, null, null, null, SonyReports.BluetoothMinimal(), null,
            "GetFeature failed.", 1, 1, 0, TimeSpan.FromMilliseconds(500));
        var pad = new SonyDeviceInspection(DualSense, "path", ConnectionType.Bluetooth, false, "sony-path-1", "device path",
            Reading(null, ChargeState.Unknown, ReadingStatus.NoData, "The pad refused the request"), read, null);

        string text = Print(pad);

        Assert.Contains("calibration  : request for 0x05 failed: GetFeature failed.", text);
        Assert.Contains("reading      : NoData: The pad refused the request", text);
        Assert.DoesNotContain("fixture", text);
    }

    [Fact]
    public void Prints_a_report_that_failed_its_crc_with_both_crcs()
    {
        byte[] rejected = SonyReports.DualSenseBluetooth(0x05);
        rejected[74] ^= 0xFF;
        var read = new SonyReadResult(SonyReadOutcome.NoBatteryReport, null, null, rejected, null, new byte[64], null,
            1, 0, 1, TimeSpan.FromMilliseconds(500));
        var pad = new SonyDeviceInspection(DualSense, "path", ConnectionType.Bluetooth, false, "sony-path-1", "device path",
            Reading(null, ChargeState.Unknown, ReadingStatus.NoData, "Bluetooth reports failed their checksum"), read, null);

        string text = Print(pad);

        uint computed = SonyCrc.Compute(0xA1, rejected.AsSpan(0, 74));
        Assert.Contains($"computed 0x{computed:X8} (seed 0xA1)", text);
        Assert.Contains($"stored 0x{computed ^ 0xFF:X8}", text);
        Assert.Contains("31 00 00 00", text);
    }

    [Fact]
    public void Prints_in_use_and_virtual_pads()
    {
        var inUse = new SonyDeviceInspection(DualSense, "path", ConnectionType.Usb, false, "sony-path-1", "device path",
            Reading(null, ChargeState.Unknown, ReadingStatus.InUseByAnotherApp, "In use by another app"), null, "Sharing violation");
        Assert.Contains("reading      : in use by another app (Sharing violation)", Print(inUse));

        var virtualPad = new SonyDeviceInspection(DualSense, "path", ConnectionType.Usb, true, "", "", null, null, null);
        Assert.Contains("skipped      : virtual pad", Print(virtualPad));
    }
}
