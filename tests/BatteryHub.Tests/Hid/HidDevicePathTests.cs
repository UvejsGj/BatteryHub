using BatteryHub.Core;
using BatteryHub.Core.Hid;

namespace BatteryHub.Tests.Hid;

public class HidDevicePathTests
{
    // Paths follow the documented shape of Windows HID interface paths. Replace them with
    // captured Probe output once real devices have been listed.
    [Theory]
    [InlineData(@"\\?\hid#vid_054c&pid_0ce6&mi_03#8&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", ConnectionType.Usb)]
    [InlineData(@"\\?\HID#VID_054C&PID_05C4#7&1F2E3D4C&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", ConnectionType.Usb)]
    [InlineData(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#9&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", ConnectionType.Bluetooth)]
    [InlineData(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02045e_pid&0b13_rev&0509_000000000000&col01#a&1b2c3d4e&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", ConnectionType.Ble)]
    [InlineData(@"\\?\hid#acpi0c50&col01#4&2c3d4e5f&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", ConnectionType.Unknown)]
    [InlineData("/dev/hidraw0", ConnectionType.Unknown)]
    public void Guesses_connection_from_path(string path, ConnectionType expected)
    {
        Assert.Equal(expected, HidDevicePath.GuessConnection(path));
    }
}
