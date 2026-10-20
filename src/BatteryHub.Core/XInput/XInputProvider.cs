using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryHub.Core.XInput;

/// <summary>The XInput calls, abstracted so the provider can be tested.</summary>
internal interface IXInputSource
{
    /// <summary>Battery information for a user index, or null when no pad is connected there.</summary>
    XInputBattery? GetGamepadBattery(int userIndex);

    /// <summary>The pad's vendor and product ID, or null when Windows does not offer them.</summary>
    XInputHardware? GetHardware(int userIndex);
}

/// <summary>
/// Xbox controllers through XInput. XInput reports one of four levels, which are shown by name, never as a percentage.
/// It gives no address, so readings are keyed by user index (slot 1-4), which can change when pads reconnect.
/// Pads on Bluetooth LE and Steam's virtual pad are left to the readers that show them better; pads on classic
/// Bluetooth show "no data", because XInput's battery data for them is unreliable.
/// </summary>
public sealed class XInputProvider : IBatteryProvider
{
    public const string ProviderName = "xinput";

    /// <summary>Shown while a first EMPTY is held back (see <see cref="XInputConstants.EmptyConfirmDelay"/>).</summary>
    public const string EmptyHeldDetail = "Waiting for the controller to confirm its battery level";

    private readonly IXInputSource _source;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Slot[] _slots = new Slot[XInputConstants.MaxUsers]; // under lock (_slots)
    private bool _unavailableLogged;

    public XInputProvider(ILogger<XInputProvider>? logger = null, TimeProvider? time = null)
        : this(new XInputNative(), logger, time)
    {
    }

    internal XInputProvider(IXInputSource source, ILogger<XInputProvider>? logger = null, TimeProvider? time = null)
    {
        _source = source;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _time = time ?? TimeProvider.System;
    }

    public string Name => ProviderName;

    public TimeSpan PollInterval { get; } = TimeSpan.FromSeconds(30);

    // XInput has no change notification.
    public event EventHandler<ReadingsChangedEventArgs>? ReadingsChanged
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<BatteryReading>> PollAsync(CancellationToken cancellationToken)
    {
        var readings = new List<BatteryReading>();
        lock (_slots)
        {
            try
            {
                for (int user = 0; user < XInputConstants.MaxUsers; user++)
                {
                    if (Read(user) is { } reading)
                    {
                        readings.Add(reading);
                    }
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // xinput1_4.dll ships with Windows 8 and later; without it there is simply nothing to show.
                if (!_unavailableLogged)
                {
                    _logger.LogInformation(ex, "XInput is not available");
                    _unavailableLogged = true;
                }
            }
        }

        return Task.FromResult<IReadOnlyList<BatteryReading>>(readings);
    }

    private BatteryReading? Read(int user)
    {
        if (_source.GetGamepadBattery(user) is not { } battery)
        {
            _slots[user] = default; // empty slot: the next pad here starts afresh
            return null;
        }

        var hardware = _source.GetHardware(user);
        if (hardware is { IsCoveredElsewhere: true })
        {
            _slots[user] = default;
            return null;
        }

        if (hardware is { IsClassicBluetooth: true })
        {
            _slots[user] = default;
            return Reading(user) with
            {
                Connection = ConnectionType.Bluetooth,
                Status = ReadingStatus.NoData,
                StatusDetail = "Windows gives no reliable battery level for Xbox controllers on classic Bluetooth",
            };
        }

        if (battery.ShownLevel is not { } level)
        {
            _slots[user] = default;
            return battery.AwaitingData ? Waiting(user) : null;
        }

        if (level == CoarseLevel.Empty && !EmptyConfirmed(ref _slots[user]))
        {
            return Reading(user) with { Status = ReadingStatus.NoData, StatusDetail = EmptyHeldDetail };
        }

        _slots[user] = new Slot(LastShown: level, FirstEmptyAt: null);
        return Reading(user) with { CoarseLevel = level };
    }

    // Some pads report EMPTY right after connecting, before their real level arrives (XBatteryStatus treats it as
    // "not reported yet"). An EMPTY counts at once after LOW (normal discharge); otherwise only once it has held for
    // a while. Polls are 30 s apart, so a pad can drop and reconnect unseen: a jump from MEDIUM or FULL straight to
    // EMPTY is treated like a fresh connection.
    private bool EmptyConfirmed(ref Slot slot)
    {
        if (slot.LastShown is CoarseLevel.Low or CoarseLevel.Empty)
        {
            return true;
        }

        var now = _time.GetUtcNow();
        slot = slot with { FirstEmptyAt = slot.FirstEmptyAt ?? now };
        return now - slot.FirstEmptyAt >= XInputConstants.EmptyConfirmDelay;
    }

    private BatteryReading Waiting(int user) => Reading(user) with
    {
        Status = ReadingStatus.NoData,
        StatusDetail = "Waiting for the controller to report its battery",
    };

    public void Dispose()
    {
    }

    private BatteryReading Reading(int user) => new()
    {
        DeviceId = $"xinput-{user + 1}",
        Name = $"Xbox controller {user + 1}",
        Kind = DeviceKind.Gamepad,
        Connection = ConnectionType.Wireless,
        Timestamp = _time.GetUtcNow(),
        Source = ProviderName,
    };

    /// <summary>What the reader remembers about the pad in one slot while it stays connected.</summary>
    /// <param name="LastShown">The level last shown for this pad.</param>
    /// <param name="FirstEmptyAt">When the EMPTY being held back was first seen.</param>
    private readonly record struct Slot(CoarseLevel? LastShown, DateTimeOffset? FirstEmptyAt);
}

/// <summary>XInputGetCapabilitiesEx's answer for one slot, as the Probe prints it.</summary>
internal readonly record struct XInputCapabilitiesDump(uint Result, byte Type, byte SubType, ushort Flags, ushort VendorId, ushort ProductId, ushort Version);

internal sealed unsafe partial class XInputNative : IXInputSource
{
    private const string Dll = "xinput1_4.dll";

    // XInputGetCapabilitiesEx is undocumented and exported only by ordinal; resolved once, null if absent.
    private static readonly delegate* unmanaged[Stdcall]<uint, uint, uint, CapabilitiesEx*, uint> GetCapabilitiesEx = ResolveCapabilitiesEx();

    public XInputBattery? GetGamepadBattery(int userIndex)
    {
        var (result, battery) = QueryBattery(userIndex);
        return result == XInputConstants.ErrorSuccess ? battery : null;
    }

    /// <summary>The raw return code too, for the Probe.</summary>
    public static (uint Result, XInputBattery Battery) QueryBattery(int userIndex)
    {
        uint result = XInputGetBatteryInformation((uint)userIndex, XInputConstants.DevTypeGamepad, out var info);
        return (result, new XInputBattery(info.BatteryType, info.BatteryLevel));
    }

    public XInputHardware? GetHardware(int userIndex)
    {
        if (GetCapabilitiesEx == null)
        {
            return null;
        }

        CapabilitiesEx caps = default;
        uint result = GetCapabilitiesEx(1, (uint)userIndex, 0, &caps);
        return result == XInputConstants.ErrorSuccess ? new XInputHardware(caps.VendorId, caps.ProductId) : null;
    }

    /// <summary>For the Probe: whether xinput1_4.dll exports XInputGetCapabilitiesEx on this Windows build.</summary>
    public static bool CapabilitiesExAvailable => GetCapabilitiesEx != null;

    /// <summary>For the Probe: XInputGetCapabilitiesEx's raw answer for a slot, or null when it is not exported.</summary>
    public static XInputCapabilitiesDump? QueryCapabilities(int userIndex)
    {
        if (GetCapabilitiesEx == null)
        {
            return null;
        }

        CapabilitiesEx caps = default;
        uint result = GetCapabilitiesEx(1, (uint)userIndex, 0, &caps);
        return new XInputCapabilitiesDump(result, caps.Type, caps.SubType, caps.Flags, caps.VendorId, caps.ProductId, caps.ProductVersion);
    }

    private static delegate* unmanaged[Stdcall]<uint, uint, uint, CapabilitiesEx*, uint> ResolveCapabilitiesEx()
    {
        try
        {
            if (!NativeLibrary.TryLoad(Dll, typeof(XInputNative).Assembly, DllImportSearchPath.System32, out nint module))
            {
                return null;
            }

            return (delegate* unmanaged[Stdcall]<uint, uint, uint, CapabilitiesEx*, uint>)GetProcAddress(module, XInputConstants.GetCapabilitiesExOrdinal);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    // XINPUT_CAPABILITIES (Type, SubType, Flags, XINPUT_GAMEPAD, XINPUT_VIBRATION) followed by the IDs.
    [StructLayout(LayoutKind.Sequential)]
    private struct CapabilitiesEx
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
        public ushort VendorId;
        public ushort ProductId;
        public ushort ProductVersion;
        public ushort Unknown1;
        public uint Unknown2;
    }

    [LibraryImport(Dll)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint XInputGetBatteryInformation(uint userIndex, byte devType, out BatteryInformation batteryInformation);

    // By ordinal: the name argument is MAKEINTRESOURCE(ordinal).
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetProcAddress(nint module, nint ordinal);
}
