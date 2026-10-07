namespace BatteryHub.Core.Sony;

/// <summary>DualShock 4 (v1, v2) and Sony wireless adapter report layout.</summary>
/// <remarks>
/// Sources: hid-playstation.c (DS4_* defines, struct dualshock4_input_report_*, dualshock4_parse_report,
/// dualshock4_get_mac_address, dongle handling) and hid-ids.h, torvalds/linux 602042b; DS4Windows DS4Device.cs,
/// schmaldeo/ds4windows ecae27a. Offsets count from the start of the report, report ID at index 0.
/// </remarks>
internal static class DualShock4Constants
{
    /// <summary>hid-ids.h USB_DEVICE_ID_SONY_PS4_CONTROLLER.</summary>
    public const ushort ProductIdV1 = 0x05C4;

    /// <summary>hid-ids.h USB_DEVICE_ID_SONY_PS4_CONTROLLER_2.</summary>
    public const ushort ProductIdV2 = 0x09CC;

    /// <summary>hid-ids.h USB_DEVICE_ID_SONY_PS4_CONTROLLER_DONGLE. USB only; relays a paired pad's USB-format reports.</summary>
    public const ushort WirelessAdapterProductId = 0x0BA0;

    /// <summary>DS4_INPUT_REPORT_USB / DS4_INPUT_REPORT_USB_SIZE.</summary>
    public const byte UsbInputReportId = 0x01;
    public const int UsbInputReportLength = 64;

    /// <summary>DS4_INPUT_REPORT_BT / DS4_INPUT_REPORT_BT_SIZE. Ends in a CRC-32 (seed 0xA1).</summary>
    public const byte BluetoothInputReportId = 0x11;
    public const int BluetoothInputReportLength = 78;

    /// <summary>DS4_INPUT_REPORT_BT_MINIMAL / _SIZE: what the pad sends over Bluetooth until switched. No battery data.</summary>
    public const byte BluetoothMinimalReportId = 0x01;
    public const int BluetoothMinimalReportLength = 10;

    /// <summary>
    /// status[0] of struct dualshock4_input_report_common sits at block offset 29. The block starts at report byte 1
    /// on USB and at byte 3 over Bluetooth (two reserved bytes after the report ID).
    /// </summary>
    public const int UsbStatusOffset = 1 + 29;
    public const int BluetoothStatusOffset = 3 + 29;

    /// <summary>status[1] on USB. Bit 2 (DS4_STATUS1_DONGLE_STATE) is set by the wireless adapter when no pad is attached.</summary>
    public const int UsbStatus1Offset = 1 + 30;
    public const byte DongleDisconnectedMask = 0x04;

    /// <summary>DS4_STATUS0_BATTERY_CAPACITY and DS4_STATUS0_CABLE_STATE. Bits 5-7 are not used.</summary>
    public const byte LevelMask = 0x0F;
    public const byte CableMask = 0x10;

    /// <summary>DS4_BATTERY_STATUS_FULL: level value meaning "charged" while the cable is in.</summary>
    public const int LevelFull = 11;

    /// <summary>With the cable in, level 14 means not charging (voltage or temperature) and 15 a charge error (hid-playstation.c comment).</summary>
    public const int LevelNotChargingVoltageOrTemperature = 14;
    public const int LevelChargeError = 15;

    /// <summary>DS4_FEATURE_REPORT_CALIBRATION_BT / _SIZE. Reading it over Bluetooth makes the pad send full 0x11 reports.</summary>
    public const byte BluetoothCalibrationFeatureReportId = 0x05;
    public const int BluetoothCalibrationFeatureReportLength = 41;

    /// <summary>DS4_FEATURE_REPORT_PAIRING_INFO / _SIZE, USB and wireless adapter only, no CRC. MAC at <see cref="SonyProtocol.PairingInfoMacOffset"/>.</summary>
    public const byte UsbPairingInfoFeatureReportId = 0x12;
    public const int UsbPairingInfoFeatureReportLength = 16;
}
