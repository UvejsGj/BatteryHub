using BatteryHub.Core;
using BatteryHub.Core.Sony;

namespace BatteryHub.Tests.Sony;

public class SonyIdResolverTests
{
    private const string UsbPortPath = @"\\?\hid#vid_054c&pid_0ce6&mi_03#8&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private static readonly SonyPadModel DualSense = SonyPadModel.Find(0x054C, 0x0CE6)!;
    private static readonly SonyPadModel DualShock4 = SonyPadModel.Find(0x054C, 0x05C4)!;
    private static readonly SonyPadModel WirelessAdapter = SonyPadModel.Find(0x054C, 0x0BA0)!;

    private readonly SonyIdResolver _resolver = new();
    private readonly FakeTime _time = new();

    private FakeSonyChannel PadWithMac(string mac, byte reportId = 0x09, int length = 20, bool crc = false) =>
        new FakeSonyChannel(_time).Feature(SonyReports.PairingReport(reportId, length, mac, crc));

    [Fact]
    public void Another_pad_in_the_same_usb_port_gets_its_own_id()
    {
        _resolver.BeginPoll(0);
        var first = _resolver.Resolve(UsbPortPath, DualSense, ConnectionType.Usb, null, PadWithMac("a0ab51000001"), true);

        _resolver.BeginPoll(0);
        var second = _resolver.Resolve(UsbPortPath, DualSense, ConnectionType.Usb, null, PadWithMac("a0ab51000002"), true);

        Assert.Equal(("sony-a0ab51000001", "pairing report 0x09"), first);
        Assert.Equal(("sony-a0ab51000002", "pairing report 0x09"), second);
    }

    [Fact]
    public void Dualshock4_usb_reads_pairing_report_0x12()
    {
        var channel = PadWithMac("a0ab51c0ffee", reportId: 0x12, length: 16);

        var id = _resolver.Resolve(UsbPortPath, DualShock4, ConnectionType.Usb, null, channel, true);

        Assert.Equal("sony-a0ab51c0ffee", id.Id);
        Assert.Equal(0x12, channel.FeatureRequests.Single().Id);
    }

    [Fact]
    public void Bluetooth_uses_the_serial_number_without_asking_the_pad()
    {
        var channel = PadWithMac("a0ab51000009", crc: true);

        var id = _resolver.Resolve("bt-path", DualSense, ConnectionType.Bluetooth, "A0AB51C0FFEE", channel, true);

        Assert.Equal(("sony-a0ab51c0ffee", "serial number"), id);
        Assert.Empty(channel.FeatureRequests);
    }

    [Fact]
    public void Bluetooth_dualsense_without_serial_falls_back_to_crc_checked_pairing_report()
    {
        var id = _resolver.Resolve("bt-path", DualSense, ConnectionType.Bluetooth, null, PadWithMac("a0ab51c0ffee", crc: true), true);

        Assert.Equal(("sony-a0ab51c0ffee", "pairing report 0x09"), id);
    }

    [Fact]
    public void Bluetooth_pairing_report_with_a_bad_crc_is_not_used_or_remembered()
    {
        byte[] report = SonyReports.PairingReport(0x09, 20, "a0ab51c0ffee", crc: true);
        report[3] ^= 0x01;
        var channel = new FakeSonyChannel(_time).Feature(report);

        Assert.Equal("device path", _resolver.Resolve("bt-path", DualSense, ConnectionType.Bluetooth, null, channel, true).Source);
        Assert.Equal("device path", _resolver.ResolveWithoutOpening("bt-path", DualSense, ConnectionType.Bluetooth, null).Source);
    }

    [Fact]
    public void Bluetooth_pad_that_must_not_be_switched_gets_no_feature_request()
    {
        var channel = PadWithMac("a0ab51c0ffee", crc: true);

        var id = _resolver.Resolve("bt-path", DualSense, ConnectionType.Bluetooth, null, channel, mayReadBluetoothFeatures: false);

        Assert.Equal("device path", id.Source);
        Assert.Empty(channel.FeatureRequests);
    }

    [Fact]
    public void Bluetooth_dualshock4_without_serial_uses_the_path()
    {
        var channel = new FakeSonyChannel(_time);

        var id = _resolver.Resolve("bt-path", DualShock4, ConnectionType.Bluetooth, null, channel, true);

        Assert.Equal((SonyIdentity.FromDevicePath("bt-path"), "device path"), id);
        Assert.Empty(channel.FeatureRequests);
    }

    [Fact]
    public void Pad_held_by_another_app_keeps_its_last_id_until_the_device_list_changes()
    {
        _resolver.BeginPoll(1);
        _resolver.Resolve(UsbPortPath, DualSense, ConnectionType.Usb, null, PadWithMac("a0ab51c0ffee"), true);

        _resolver.BeginPoll(1);
        Assert.Equal(("sony-a0ab51c0ffee", "pairing report 0x09, earlier read"),
            _resolver.ResolveWithoutOpening(UsbPortPath, DualSense, ConnectionType.Usb, null));

        _resolver.BeginPoll(2);
        Assert.Equal("device path", _resolver.ResolveWithoutOpening(UsbPortPath, DualSense, ConnectionType.Usb, null).Source);
    }

    [Fact]
    public void Bluetooth_pad_held_by_another_app_is_still_named_by_its_serial()
    {
        var id = _resolver.ResolveWithoutOpening("bt-path", DualShock4, ConnectionType.Bluetooth, "a0ab51c0ffee");

        Assert.Equal(("sony-a0ab51c0ffee", "serial number"), id);
    }

    [Fact]
    public void Wireless_adapter_is_never_remembered()
    {
        // The adapter can be paired to another pad without its path changing.
        _resolver.Resolve(UsbPortPath, WirelessAdapter, ConnectionType.Usb, null, PadWithMac("a0ab51c0ffee", reportId: 0x12, length: 16), true);

        Assert.Equal("device path", _resolver.ResolveWithoutOpening(UsbPortPath, WirelessAdapter, ConnectionType.Usb, null).Source);
    }

    [Fact]
    public void Refused_feature_request_falls_back_to_the_path()
    {
        var channel = new FakeSonyChannel(_time) { FeatureError = new IOException("GetFeature failed.") };

        Assert.Equal("device path", _resolver.Resolve(UsbPortPath, DualSense, ConnectionType.Usb, null, channel, true).Source);
    }
}
