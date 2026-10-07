using System.Globalization;

namespace BatteryHub.Core.Presentation;

/// <summary>Turns readings into the text and numbers the tray UI shows. No UI types, so it is unit tested.</summary>
public static class DeviceListPresenter
{
    /// <summary>Windows cuts tray tooltips at 127 characters (NOTIFYICONDATA.szTip).</summary>
    public const int MaxToolTipLength = 127;

    /// <summary>Level at or below which a discharging device is marked low. Milestone 6 makes it configurable.</summary>
    public const int DefaultLowPercent = 20;

    public static IReadOnlyList<DeviceRow> Rows(IEnumerable<BatteryReading> readings, int lowPercent = DefaultLowPercent) =>
        readings.Select(r => Row(r, lowPercent)).ToList();

    public static DeviceRow Row(BatteryReading reading, int lowPercent = DefaultLowPercent)
    {
        string connection = Connection(reading.Connection);
        string? charge = Charge(reading.ChargeState);
        string details = charge is null ? connection : $"{connection} · {charge}";

        return reading.Status switch
        {
            ReadingStatus.Ok when reading.Percent is int percent => new(
                reading.DeviceId,
                reading.Name,
                percent.ToString(CultureInfo.CurrentCulture) + "%",
                details,
                null,
                percent / 100.0,
                percent <= lowPercent && reading.ChargeState == ChargeState.Discharging),
            ReadingStatus.Ok when reading.CoarseLevel is CoarseLevel coarse => new(
                reading.DeviceId,
                reading.Name,
                Coarse(coarse),
                details,
                null,
                CoarseFraction(coarse),
                coarse <= CoarseLevel.Low && reading.ChargeState == ChargeState.Discharging),
            ReadingStatus.InUseByAnotherApp => new(reading.DeviceId, reading.Name, "In use", connection, reading.StatusDetail ?? "In use by another app", null, false),
            ReadingStatus.DeviceError => new(reading.DeviceId, reading.Name, "Fault", connection, reading.StatusDetail ?? "The device reports a battery fault", null, false),
            _ => new(reading.DeviceId, reading.Name, "No data", connection, reading.StatusDetail ?? "The device did not report its battery", null, false),
        };
    }

    /// <summary>One line per device, e.g. "DualShock 4: 85% (charging)", cut to fit the tray tooltip.</summary>
    public static string ToolTip(IEnumerable<BatteryReading> readings)
    {
        var lines = readings.Select(r =>
        {
            var row = Row(r);
            return r.ChargeState is ChargeState.Charging or ChargeState.Full && row.Problem is null
                ? $"{r.Name}: {row.Level} ({Charge(r.ChargeState)!.ToLower(CultureInfo.CurrentCulture)})"
                : $"{r.Name}: {row.Level}";
        }).ToList();

        string text = lines.Count == 0 ? "BatteryHub: no devices" : string.Join("\n", lines);
        return text.Length <= MaxToolTipLength ? text : string.Concat(text.AsSpan(0, MaxToolTipLength - 1), "…");
    }

    public static string Connection(ConnectionType connection) => connection switch
    {
        ConnectionType.Usb => "USB",
        ConnectionType.Bluetooth => "Bluetooth",
        ConnectionType.Ble => "Bluetooth LE",
        _ => "Unknown connection",
    };

    private static string? Charge(ChargeState state) => state switch
    {
        ChargeState.Charging => "Charging",
        ChargeState.Full => "Full",
        _ => null,
    };

    private static string Coarse(CoarseLevel level) => level switch
    {
        CoarseLevel.Empty => "Empty",
        CoarseLevel.Low => "Low",
        CoarseLevel.Medium => "Medium",
        _ => "Full",
    };

    // Bar lengths for coarse levels are a drawing choice only; the text never shows a percentage for them.
    private static double CoarseFraction(CoarseLevel level) => level switch
    {
        CoarseLevel.Empty => 0.05,
        CoarseLevel.Low => 0.25,
        CoarseLevel.Medium => 0.6,
        _ => 1.0,
    };
}
