namespace BatteryHub.Core;

/// <summary>A source of battery readings for one family of devices.</summary>
public interface IBatteryProvider : IDisposable
{
    /// <summary>Short name used in logs and in <see cref="BatteryReading.Source"/>.</summary>
    string Name { get; }

    /// <summary>How often the monitor should call <see cref="PollAsync"/>.</summary>
    TimeSpan PollInterval { get; }

    /// <summary>Returns a reading for every device this provider currently sees.</summary>
    Task<IReadOnlyList<BatteryReading>> PollAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Raised when the provider learns of a change between polls (device plugged in, push notification).
    /// Carries the provider's full current set of readings, not a delta. Providers without a push source never raise it.
    /// </summary>
    event EventHandler<ReadingsChangedEventArgs>? ReadingsChanged;
}

public sealed class ReadingsChangedEventArgs(IReadOnlyList<BatteryReading> readings) : EventArgs
{
    public IReadOnlyList<BatteryReading> Readings { get; } = readings;
}
