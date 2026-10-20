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

    /// <summary>XINPUT_CAPS_WIRELESS in XINPUT_CAPABILITIES.Flags.</summary>
    public const ushort CapsWireless = 0x0002;

    public const uint ErrorSuccess = 0;
    public const uint ErrorDeviceNotConnected = 1167;

    /// <summary>Undocumented XInputGetCapabilitiesEx, exported by ordinal from xinput1_4.dll (as SDL uses it).</summary>
    public const int GetCapabilitiesExOrdinal = 108;

    public const ushort MicrosoftVendorId = 0x045E;

    /// <summary>
    /// Xbox controllers on Bluetooth LE (firmware 5 and later): Series X|S and most models report 0x0B13, One S
    /// 0x0B20, Adaptive 0x0B21, Elite 2 0x0B22 (SDL SDL_IsJoystickBluetoothXboxOne, xpadneo docs). XInput's battery
    /// data for pads on Bluetooth is unreliable; the Bluetooth readers show these with a percentage instead.
    /// </summary>
    public static readonly ushort[] BluetoothLowEnergyProductIds = [0x0B13, 0x0B20, 0x0B21, 0x0B22];

    /// <summary>
    /// Xbox controllers on classic Bluetooth (older firmware): One S 0x02E0 and 0x02FD, Elite 2 0x0B05, Adaptive
    /// 0x0B0C (same sources). XInput's battery data for them is unreliable and no other reader has found a source yet.
    /// </summary>
    public static readonly ushort[] ClassicBluetoothProductIds = [0x02E0, 0x02FD, 0x0B05, 0x0B0C];

    /// <summary>A newly connected pad can report EMPTY before its real level; an EMPTY is shown once it holds this long.</summary>
    public static readonly TimeSpan EmptyConfirmDelay = TimeSpan.FromSeconds(10);

    /// <summary>Steam Input's virtual gamepad: it stands in for a pad another reader already shows.</summary>
    public const ushort ValveVendorId = 0x28DE;
    public const ushort SteamVirtualGamepadProductId = 0x11FF;
}
