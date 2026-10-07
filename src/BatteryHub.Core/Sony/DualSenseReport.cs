namespace BatteryHub.Core.Sony;

/// <summary>Pure parsers for DualSense input reports.</summary>
public static class DualSenseReport
{
    /// <summary>Parses a USB input report 0x01 (64 bytes, report ID at index 0).</summary>
    public static bool TryParseUsb(ReadOnlySpan<byte> report, out SonyBatteryStatus status)
    {
        status = default;
        if (report.Length < DualSenseConstants.UsbInputReportLength || report[0] != DualSenseConstants.UsbInputReportId)
        {
            return false;
        }

        status = DecodeStatus(report[DualSenseConstants.UsbStatusOffset]);
        return true;
    }

    /// <summary>Parses a Bluetooth input report 0x31 (78 bytes, report ID at index 0). Rejects it if the CRC does not match.</summary>
    public static bool TryParseBluetooth(ReadOnlySpan<byte> report, out SonyBatteryStatus status)
    {
        status = default;
        if (report.Length < DualSenseConstants.BluetoothInputReportLength
            || report[0] != DualSenseConstants.BluetoothInputReportId
            || !SonyCrc.IsValid(report, DualSenseConstants.BluetoothInputReportLength, SonyProtocol.InputCrcSeed))
        {
            return false;
        }

        status = DecodeStatus(report[DualSenseConstants.BluetoothStatusOffset]);
        return true;
    }

    /// <summary>Decodes the status byte: low nibble level 0-10, high nibble charging state.</summary>
    /// <remarks>Mapping from hid-playstation.c dualsense_parse_report.</remarks>
    public static SonyBatteryStatus DecodeStatus(byte value)
    {
        int level = value & DualSenseConstants.LevelMask;
        int charging = value >> DualSenseConstants.ChargingShift;
        return charging switch
        {
            DualSenseConstants.ChargingDischarging => new(SonyBatteryStatus.LevelToPercent(level), ChargeState.Discharging, null, value),
            DualSenseConstants.ChargingCharging => new(SonyBatteryStatus.LevelToPercent(level), ChargeState.Charging, null, value),
            DualSenseConstants.ChargingFull => new(100, ChargeState.Full, null, value),
            DualSenseConstants.ChargingVoltageOrTemperatureOutOfRange => new(null, ChargeState.Unknown, "Not charging: voltage or temperature out of range", value),
            DualSenseConstants.ChargingTemperatureError => new(null, ChargeState.Unknown, "Not charging: temperature error", value),
            DualSenseConstants.ChargingError => new(null, ChargeState.Unknown, "Charging error", value),
            _ => new(null, ChargeState.Unknown, $"Unknown charging state 0x{charging:X}", value),
        };
    }
}
