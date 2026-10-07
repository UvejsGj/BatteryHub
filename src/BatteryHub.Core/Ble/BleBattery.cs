namespace BatteryHub.Core.Ble;

/// <summary>Pure helpers for Bluetooth LE battery readings.</summary>
/// <remarks>
/// Sources: Bluetooth SIG Battery Service 1.0 (Battery Level 0x2A19: uint8 percent, 0-100) and Assigned Numbers
/// (Appearance values: category in bits 15-6, subcategory in bits 5-0).
/// </remarks>
public static class BleBattery
{
    /// <summary>A Battery Level value is a percentage; anything above 100 is invalid and is not shown.</summary>
    public static int? Percent(byte value) => value <= 100 ? value : null;

    // Appearance categories (value >> 6) and subcategories, from Bluetooth SIG Assigned Numbers
    // (assigned_numbers/core/appearance_values.yaml).
    private const int CategoryHid = 0x00F;
    private const int CategoryWearableAudio = 0x025;
    private const int CategoryHearingAid = 0x029;
    private const int HidKeyboard = 0x01;
    private const int HidMouse = 0x02;
    private const int HidJoystick = 0x03;
    private const int HidGamepad = 0x04;
    private const int AudioEarbud = 0x01;
    private const int AudioHeadset = 0x02;
    private const int AudioHeadphones = 0x03;
    private const int AudioNeckBand = 0x04;
    private const int AudioLeftEarbud = 0x05;
    private const int AudioRightEarbud = 0x06;

    public static DeviceKind KindFromAppearance(ushort? appearance)
    {
        if (appearance is not { } value)
        {
            return DeviceKind.Other;
        }

        int category = value >> 6;
        int subcategory = value & 0x3F;
        return category switch
        {
            CategoryHid => subcategory switch
            {
                HidKeyboard => DeviceKind.Keyboard,
                HidMouse => DeviceKind.Mouse,
                HidJoystick or HidGamepad => DeviceKind.Gamepad,
                _ => DeviceKind.Other,
            },
            CategoryWearableAudio => subcategory switch
            {
                AudioEarbud or AudioLeftEarbud or AudioRightEarbud => DeviceKind.Earbuds,
                AudioHeadset or AudioHeadphones or AudioNeckBand => DeviceKind.Headset,
                _ => DeviceKind.Other,
            },
            CategoryHearingAid => DeviceKind.Earbuds,
            _ => DeviceKind.Other,
        };
    }
}
