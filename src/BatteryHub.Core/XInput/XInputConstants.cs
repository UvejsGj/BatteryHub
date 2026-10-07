namespace BatteryHub.Core.XInput;

/// <summary>XInput battery values and the hardware IDs XInput readings are filtered by.</summary>
/// <remarks>
/// Sources: Windows SDK um/Xinput.h (via microsoft/win32metadata) and the XInputGetBatteryInformation docs for the
/// values; SDL (src/joystick/usb_ids.h, SDL_xinputjoystick.c) and the Linux hid-ids.h for the hardware IDs.
/// </remarks>
internal static class XInputConstants
{
    /// <summary>XUSER_MAX_COUNT: XInput user indexes 0-3.</summary>
    public const int MaxUsers = 4;

    public const byte DevTypeGamepad = 0x00;

    public const byte TypeDisconnected = 0x00;
    public const byte TypeWired = 0x01;
    public const byte TypeAlkaline = 0x02;
    public const byte TypeNiMH = 0x03;
    public const byte TypeUnknown = 0xFF;

    public const byte LevelEmpty = 0x00;
    public const byte LevelLow = 0x01;
    public const byte LevelMedium = 0x02;
    public const byte LevelFull = 0x03;

    public const uint ErrorSuccess = 0;
    public const uint ErrorDeviceNotConnected = 1167;

    /// <summary>Undocumented XInputGetCapabilitiesEx, exported by ordinal from xinput1_4.dll (as SDL uses it).</summary>
    public const int GetCapabilitiesExOrdinal = 108;

    public const ushort MicrosoftVendorId = 0x045E;

    /// <summary>
    /// Xbox controller product IDs used only over Bluetooth. XInput's battery data for these is unreliable, and
    /// Windows' own Bluetooth battery value (read by the Bluetooth readers) gives a percentage instead.
    /// </summary>
    public static readonly ushort[] BluetoothOnlyProductIds = [0x02E0, 0x02FD, 0x0B05, 0x0B0C, 0x0B13, 0x0B20, 0x0B21, 0x0B22];

    /// <summary>Steam Input's virtual gamepad: it stands in for a pad another reader already shows.</summary>
    public const ushort ValveVendorId = 0x28DE;
    public const ushort SteamVirtualGamepadProductId = 0x11FF;
}
