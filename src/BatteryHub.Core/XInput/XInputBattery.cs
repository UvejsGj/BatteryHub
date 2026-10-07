namespace BatteryHub.Core.XInput;

/// <summary>XINPUT_BATTERY_INFORMATION as returned for one user index.</summary>
public readonly record struct XInputBattery(byte Type, byte Level)
{
    /// <summary>A pad is in the slot but has not reported its battery yet (common for ~10 s after it connects).</summary>
    public bool AwaitingData => Type == XInputConstants.TypeDisconnected;

    /// <summary>
    /// The coarse level, or null when there is none to show: a wired pad (no battery; this includes the virtual pads
    /// DS4Windows creates, which other readers already show), an unknown battery type, or no data yet. XInput has
    /// four levels and no charging information; BatteryHub never turns them into a percentage.
    /// </summary>
    public CoarseLevel? ShownLevel => Type is XInputConstants.TypeAlkaline or XInputConstants.TypeNiMH
        ? Level switch
        {
            XInputConstants.LevelEmpty => CoarseLevel.Empty,
            XInputConstants.LevelLow => CoarseLevel.Low,
            XInputConstants.LevelMedium => CoarseLevel.Medium,
            XInputConstants.LevelFull => CoarseLevel.Full,
            _ => null,
        }
        : null;
}

/// <summary>Vendor and product ID of the pad in an XInput slot, from XInputGetCapabilitiesEx.</summary>
public readonly record struct XInputHardware(ushort VendorId, ushort ProductId)
{
    /// <summary>
    /// True for pads another reader shows better: Xbox controllers on Bluetooth (the Bluetooth readers give a
    /// percentage) and Steam Input's virtual gamepad (it mirrors a real pad).
    /// </summary>
    public bool IsCoveredElsewhere =>
        (VendorId == XInputConstants.MicrosoftVendorId && XInputConstants.BluetoothOnlyProductIds.Contains(ProductId))
        || (VendorId == XInputConstants.ValveVendorId && ProductId == XInputConstants.SteamVirtualGamepadProductId);
}
