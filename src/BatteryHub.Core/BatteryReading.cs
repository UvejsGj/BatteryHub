namespace BatteryHub.Core;

public enum DeviceKind
{
    Other,
    Gamepad,
    Headset,
    Earbuds,
    Mouse,
    Keyboard,
}

/// <summary>Battery level for sources that report a few steps instead of a percentage.</summary>
public enum CoarseLevel
{
    Empty,
    Low,
    Medium,
    Full,
}

public enum ChargeState
{
    Unknown,
    Discharging,
    Charging,
    Full,
}

public enum ReadingStatus
{
    /// <summary>The reading carries a level (<see cref="BatteryReading.Percent"/> or <see cref="BatteryReading.CoarseLevel"/>).</summary>
    Ok,

    /// <summary>The device could not be opened because another app holds it exclusively.</summary>
    InUseByAnotherApp,

    /// <summary>The device answered, but sent nothing that carries battery data.</summary>
    NoData,

    /// <summary>The device reported a battery or charging fault.</summary>
    DeviceError,
}

/// <summary>Part of a device with its own battery, e.g. an earbud or a charging case.</summary>
public sealed record BatteryComponent(string Name, int? Percent, ChargeState ChargeState);

/// <summary>One device's battery state as one provider saw it at <see cref="Timestamp"/>.</summary>
public sealed record BatteryReading
{
    /// <summary>Stable across polls and reconnects; used to de-duplicate and to key per-device settings.</summary>
    public required string DeviceId { get; init; }

    public required string Name { get; init; }

    public required DeviceKind Kind { get; init; }

    public required ConnectionType Connection { get; init; }

    /// <summary>0-100 when the source reports a percentage. Never derived from a coarse level.</summary>
    public int? Percent
    {
        get;
        init => field = value is null or (>= 0 and <= 100)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Percent), value, "Percent must be 0-100.");
    }

    /// <summary>Set by sources that only report a few levels.</summary>
    public CoarseLevel? CoarseLevel { get; init; }

    public ChargeState ChargeState { get; init; } = ChargeState.Unknown;

    public IReadOnlyList<BatteryComponent> Components { get; init; } = [];

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Name of the provider that produced the reading.</summary>
    public required string Source { get; init; }

    public ReadingStatus Status { get; init; } = ReadingStatus.Ok;

    /// <summary>Why <see cref="Status"/> is not <see cref="ReadingStatus.Ok"/>, in words a user can act on.</summary>
    public string? StatusDetail { get; init; }
}
