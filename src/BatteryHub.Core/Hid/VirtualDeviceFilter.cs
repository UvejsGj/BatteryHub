namespace BatteryHub.Core.Hid;

/// <summary>One node in the Windows device tree.</summary>
public sealed record DeviceNode(string InstanceId, IReadOnlyList<string> HardwareIds);

/// <summary>
/// Spots HID devices that software created, such as the DualShock 4 that DS4Windows emulates through ViGEm. Those
/// report a real pad's VID and PID, so without this they would show up as a second, phantom pad.
/// </summary>
/// <remarks>
/// Rule from DS4Windows (ScpUtil.cs CheckIfVirtualDevice): walk up from the device to the node just below the root.
/// The device is virtual if that node's instance ID starts with ROOT\SYSTEM or ROOT\USB (root-enumerated virtual
/// buses such as ViGEmBus), unless a node on the way has a hardware ID of a bus that relays real devices.
/// </remarks>
public static class VirtualDeviceFilter
{
    private static readonly string[] VirtualRootPrefixes = [@"ROOT\SYSTEM", @"ROOT\USB"];

    // Buses whose children are real pads seen through another layer: reWASD (ROOT\HIDGAMEMAP) and VirtualHere USB
    // sharing (ROOT\VHUSB3HC). Same list as DS4Windows.
    private static readonly string[] RelayHardwareIds = [@"ROOT\HIDGAMEMAP", @"ROOT\VHUSB3HC"];

    /// <param name="ancestry">The device first, then each parent, ending with the node just below HTREE\ROOT\0.</param>
    public static bool IsVirtual(IReadOnlyList<DeviceNode> ancestry)
    {
        if (ancestry.Count == 0)
        {
            return false;
        }

        foreach (var node in ancestry)
        {
            if (node.HardwareIds.Any(id => RelayHardwareIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        string top = ancestry[^1].InstanceId;
        return VirtualRootPrefixes.Any(prefix => top.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
