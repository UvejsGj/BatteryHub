namespace BatteryHub.Core.Presentation;

/// <summary>One line of the flyout, ready to bind.</summary>
/// <param name="Level">"85%", a coarse level such as "Medium", or a short status such as "In use".</param>
/// <param name="Details">Connection and charge state, e.g. "Bluetooth · Charging".</param>
/// <param name="Problem">Why there is no level, in words a user can act on; null when there is one.</param>
/// <param name="BarFraction">0-1 for the level bar; null when there is nothing to draw.</param>
/// <param name="IsLow">At or below the low-battery threshold and not charging.</param>
public sealed record DeviceRow(
    string DeviceId,
    string Name,
    string Level,
    string Details,
    string? Problem,
    double? BarFraction,
    bool IsLow);
