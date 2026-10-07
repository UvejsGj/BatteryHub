namespace BatteryHub.Core.Settings;

/// <summary>User settings, stored as JSON. Missing properties take these defaults, unknown ones are ignored.</summary>
public sealed record AppSettings
{
    /// <summary>
    /// Read Bluetooth PlayStation controllers by switching them to full reports
    /// (see <see cref="Sony.SonyHidOptions.RequestFullBluetoothReports"/>).
    /// </summary>
    public bool ReadBluetoothPlayStationControllers { get; init; } = true;
}
