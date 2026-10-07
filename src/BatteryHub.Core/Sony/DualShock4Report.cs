namespace BatteryHub.Core.Sony;

/// <summary>Pure parsers for DualShock 4 input reports.</summary>
public static class DualShock4Report
{
    /// <summary>Parses a USB input report 0x01 (64 bytes, report ID at index 0). Also used for the wireless adapter.</summary>
    public static bool TryParseUsb(ReadOnlySpan<byte> report, out SonyBatteryStatus status)
    {
        status = default;
        if (report.Length < DualShock4Constants.UsbInputReportLength || report[0] != DualShock4Constants.UsbInputReportId)
        {
            return false;
        }

        status = DecodeStatus(report[DualShock4Constants.UsbStatusOffset]);
        return true;
    }

    /// <summary>Parses a Bluetooth input report 0x11 (78 bytes, report ID at index 0). Rejects it if the CRC does not match.</summary>
    public static bool TryParseBluetooth(ReadOnlySpan<byte> report, out SonyBatteryStatus status)
    {
        status = default;
        if (report.Length < DualShock4Constants.BluetoothInputReportLength
            || report[0] != DualShock4Constants.BluetoothInputReportId
            || !SonyCrc.IsValid(report, DualShock4Constants.BluetoothInputReportLength, SonyProtocol.InputCrcSeed))
        {
            return false;
        }

        status = DecodeStatus(report[DualShock4Constants.BluetoothStatusOffset]);
        return true;
    }

    /// <summary>
    /// True when a wireless adapter's USB report says no pad is attached. Its other bytes are then zeros and must not be
    /// read as a 5% battery.
    /// </summary>
    public static bool IsWirelessAdapterEmpty(ReadOnlySpan<byte> report) =>
        report.Length > DualShock4Constants.UsbStatus1Offset
        && (report[DualShock4Constants.UsbStatus1Offset] & DualShock4Constants.DongleDisconnectedMask) != 0;

    /// <summary>Decodes status[0]: low nibble level, bit 4 cable connected.</summary>
    /// <remarks>Mapping from hid-playstation.c dualshock4_parse_report.</remarks>
    public static SonyBatteryStatus DecodeStatus(byte value)
    {
        int level = value & DualShock4Constants.LevelMask;
        bool cable = (value & DualShock4Constants.CableMask) != 0;

        if (!cable)
        {
            return new(level < 10 ? SonyBatteryStatus.LevelToPercent(level) : 100, ChargeState.Discharging, null, value);
        }

        return level switch
        {
            < 10 => new(SonyBatteryStatus.LevelToPercent(level), ChargeState.Charging, null, value),
            10 => new(100, ChargeState.Charging, null, value),
            DualShock4Constants.LevelFull => new(100, ChargeState.Full, null, value),
            DualShock4Constants.LevelNotChargingVoltageOrTemperature => new(null, ChargeState.Unknown, "Not charging: voltage or temperature out of range", value),
            DualShock4Constants.LevelChargeError => new(null, ChargeState.Unknown, "Charging error", value),
            _ => new(null, ChargeState.Unknown, $"Unknown charging state (level {level} with cable)", value),
        };
    }
}
