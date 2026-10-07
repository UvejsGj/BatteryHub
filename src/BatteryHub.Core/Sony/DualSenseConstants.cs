namespace BatteryHub.Core.Sony;

/// <summary>DualSense and DualSense Edge report layout.</summary>
/// <remarks>
/// Sources: hid-playstation.c (DS_* defines, struct dualsense_input_report, dualsense_parse_report,
/// dualsense_get_mac_address) and hid-ids.h, torvalds/linux 602042b; DS4Windows DualSenseDevice.cs, schmaldeo/ds4windows ecae27a.
/// Offsets count from the start of the report, report ID at index 0.
/// </remarks>
internal static class DualSenseConstants
{
    /// <summary>hid-ids.h USB_DEVICE_ID_SONY_PS5_CONTROLLER.</summary>
    public const ushort ProductId = 0x0CE6;

    /// <summary>hid-ids.h USB_DEVICE_ID_SONY_PS5_CONTROLLER_2. Same report layout as the DualSense.</summary>
    public const ushort EdgeProductId = 0x0DF2;

    /// <summary>DS_INPUT_REPORT_USB / DS_INPUT_REPORT_USB_SIZE.</summary>
    public const byte UsbInputReportId = 0x01;
    public const int UsbInputReportLength = 64;

    /// <summary>DS_INPUT_REPORT_BT / DS_INPUT_REPORT_BT_SIZE. Ends in a CRC-32 (seed 0xA1).</summary>
    public const byte BluetoothInputReportId = 0x31;
    public const int BluetoothInputReportLength = 78;

    /// <summary>
    /// status[0] of struct dualsense_input_report sits at struct offset 52. The struct starts at report byte 1 on USB
    /// and at byte 2 over Bluetooth (one header byte after the report ID).
    /// </summary>
    public const int UsbStatusOffset = 1 + 52;
    public const int BluetoothStatusOffset = 2 + 52;

    /// <summary>DS_FEATURE_REPORT_CALIBRATION / _SIZE, same on both transports. Reading it over Bluetooth makes the pad send full 0x31 reports.</summary>
    public const byte CalibrationFeatureReportId = 0x05;
    public const int CalibrationFeatureReportLength = 41;

    /// <summary>DS_FEATURE_REPORT_PAIRING_INFO / _SIZE. MAC at <see cref="SonyProtocol.PairingInfoMacOffset"/>.</summary>
    public const byte PairingInfoFeatureReportId = 0x09;
    public const int PairingInfoFeatureReportLength = 20;

    /// <summary>DS_STATUS0_BATTERY_CAPACITY: low nibble of the status byte, level 0-10.</summary>
    public const byte LevelMask = 0x0F;

    /// <summary>DS_STATUS0_CHARGING: high nibble of the status byte.</summary>
    public const int ChargingShift = 4;

    public const int ChargingDischarging = 0x0;
    public const int ChargingCharging = 0x1;
    public const int ChargingFull = 0x2;
    public const int ChargingVoltageOrTemperatureOutOfRange = 0xA;
    public const int ChargingTemperatureError = 0xB;
    public const int ChargingError = 0xF;
}
