using BatteryHub.Core.Hid;

namespace BatteryHub.Tests.Hid;

public class VirtualDeviceFilterTests
{
    private static DeviceNode Node(string id, params string[] hardwareIds) => new(id, hardwareIds);

    [Fact]
    public void Pad_emulated_on_a_root_enumerated_bus_is_virtual()
    {
        // Shape of a ViGEm DualShock 4: HID collection, its USB device, then ViGEmBus under ROOT\SYSTEM.
        DeviceNode[] chain =
        [
            Node(@"HID\VID_054C&PID_05C4\9&1A2B3C4D&0&0000", @"HID\VID_054C&PID_05C4"),
            Node(@"USB\VID_054C&PID_05C4\1&2D595CA7&0&01", @"USB\VID_054C&PID_05C4"),
            Node(@"ROOT\SYSTEM\0001", @"Nefarius\ViGEmBus\Gen1"),
        ];

        Assert.True(VirtualDeviceFilter.IsVirtual(chain));
    }

    [Fact]
    public void Root_usb_prefix_is_virtual_too()
    {
        Assert.True(VirtualDeviceFilter.IsVirtual([Node(@"HID\X\1"), Node(@"root\usb\0000")]));
    }

    [Fact]
    public void Pad_on_a_real_usb_controller_is_not_virtual()
    {
        DeviceNode[] chain =
        [
            Node(@"HID\VID_054C&PID_0CE6&MI_03\8&2A3B4C5D&0&0000"),
            Node(@"USB\VID_054C&PID_0CE6&MI_03\7&1F2E3D4C&0&0003"),
            Node(@"USB\VID_054C&PID_0CE6\1A2B3C4D5E6F"),
            Node(@"USB\ROOT_HUB30\4&2B3C4D5E&0&0"),
            Node(@"PCI\VEN_8086&DEV_A36D&SUBSYS_00000000&REV_10\3&11583659&0&A0"),
            Node(@"ACPI\PNP0A08\0"),
            Node(@"ACPI_HAL\PNP0C08\0"),
            Node(@"ROOT\ACPI_HAL\0000"), // real hardware hangs off the ACPI HAL, itself a ROOT\ node
        ];

        Assert.False(VirtualDeviceFilter.IsVirtual(chain));
    }

    [Theory]
    [InlineData(@"ROOT\HIDGAMEMAP")]
    [InlineData(@"root\vhusb3hc")]
    public void Buses_that_relay_real_pads_are_not_virtual(string relayHardwareId)
    {
        DeviceNode[] chain =
        [
            Node(@"HID\VID_054C&PID_05C4\1"),
            Node(@"ROOT\SYSTEM\0002", relayHardwareId),
        ];

        Assert.False(VirtualDeviceFilter.IsVirtual(chain));
    }

    [Fact]
    public void Empty_ancestry_is_not_virtual()
    {
        Assert.False(VirtualDeviceFilter.IsVirtual([]));
    }
}
