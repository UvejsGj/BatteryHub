namespace BatteryHub.Core.Sony;

/// <summary>What a Sony pad's battery status byte says.</summary>
/// <param name="Percent">Null when the pad reports a fault; Sony pads report in 10% steps.</param>
/// <param name="Fault">Set when the pad reports a battery or charging fault, in words a user can act on.</param>
/// <param name="StatusByte">The raw byte, kept for the Probe.</param>
public readonly record struct SonyBatteryStatus(int? Percent, ChargeState ChargeState, string? Fault, byte StatusByte)
{
    /// <summary>Low nibble of the status byte on both pads.</summary>
    public int Level => StatusByte & DualSenseConstants.LevelMask;

    /// <summary>DualShock 4 only: bit 4, USB cable connected.</summary>
    public bool CableConnected => (StatusByte & DualShock4Constants.CableMask) != 0;

    internal static int LevelToPercent(int level) => Math.Min(level * 10 + 5, 100);
}
