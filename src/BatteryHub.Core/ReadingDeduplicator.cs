namespace BatteryHub.Core;

/// <summary>Merges every provider's readings into one list with one reading per device.</summary>
/// <remarks>
/// Providers that can identify a device by its Bluetooth address use the same ID for it
/// (<see cref="DeviceIds.Bluetooth"/>), so one headset seen through two readers collapses here. When two readings share
/// an ID, the more informative one wins: a percentage over a coarse level over a fault over "in use" over "no data".
/// Ties go to the provider listed first.
/// </remarks>
public static class ReadingDeduplicator
{
    public static List<BatteryReading> Merge(IEnumerable<IReadOnlyList<BatteryReading>> readingsByProvider)
    {
        var best = new Dictionary<string, (BatteryReading Reading, int Rank)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var readings in readingsByProvider)
        {
            foreach (var reading in readings)
            {
                int rank = Rank(reading);
                if (!best.TryGetValue(reading.DeviceId, out var current))
                {
                    order.Add(reading.DeviceId);
                    best[reading.DeviceId] = (reading, rank);
                }
                else if (rank > current.Rank)
                {
                    best[reading.DeviceId] = (reading, rank);
                }
            }
        }

        return order.Select(id => best[id].Reading).ToList();
    }

    /// <summary>How much a reading tells the user; higher wins.</summary>
    public static int Rank(BatteryReading reading) => reading.Status switch
    {
        ReadingStatus.Ok when reading.Percent is not null => 5,
        ReadingStatus.Ok when reading.CoarseLevel is not null => 4,
        ReadingStatus.DeviceError => 3,
        ReadingStatus.InUseByAnotherApp => 2,
        ReadingStatus.NoData => 1,
        _ => 0,
    };
}
