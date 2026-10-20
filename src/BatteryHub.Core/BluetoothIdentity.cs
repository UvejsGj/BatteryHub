namespace BatteryHub.Core;

/// <summary>Which address a Bluetooth device is keyed by, so every reader gives one physical device the same ID.</summary>
public static class BluetoothIdentity
{
    // The container Windows puts the PC's own devices in.
    private static readonly Guid LocalMachineContainer = new("00000000-0000-0000-ffff-ffffffffffff");

    /// <summary>
    /// A dual-mode device (e.g. a headset with LE Audio) can use a different address on Bluetooth LE than on classic
    /// Bluetooth; Windows groups both halves under one container ID. Returns the address of the one paired classic
    /// device in <paramref name="container"/>, or null when there is none, or more than one, or no usable container.
    /// The classic address is the one Settings shows, so LE readings of the device are keyed by it.
    /// </summary>
    public static ulong? ClassicAddressInContainer(Guid? container, IEnumerable<(ulong Address, Guid? Container)> classicDevices)
    {
        if (container is not { } id || id == Guid.Empty || id == LocalMachineContainer)
        {
            return null;
        }

        var matches = classicDevices.Where(d => d.Container == id).Select(d => d.Address).Distinct().Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
