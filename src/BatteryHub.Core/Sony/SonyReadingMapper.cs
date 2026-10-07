namespace BatteryHub.Core.Sony;

/// <summary>Turns what a read saw into the reading the user sees.</summary>
internal static class SonyReadingMapper
{
    /// <returns>Null when there is nothing to show (a wireless adapter with no pad).</returns>
    public static BatteryReading? FromRead(SonyReadResult read, SonyPadModel model, ConnectionType connection, string id, DateTimeOffset timestamp, bool requestFullBluetoothReports)
    {
        var reading = New(model, connection, id, timestamp);
        switch (read.Outcome)
        {
            case SonyReadOutcome.Battery when read.Battery is { } battery:
                return reading with
                {
                    Percent = battery.Percent,
                    ChargeState = battery.ChargeState,
                    Status = battery.Fault is null ? ReadingStatus.Ok : ReadingStatus.DeviceError,
                    StatusDetail = battery.Fault,
                };
            case SonyReadOutcome.WirelessAdapterEmpty:
                return null;
            default:
                return reading with { Status = ReadingStatus.NoData, StatusDetail = DescribeNoData(read, connection, requestFullBluetoothReports) };
        }
    }

    public static BatteryReading InUse(SonyPadModel model, ConnectionType connection, string id, DateTimeOffset timestamp) =>
        New(model, connection, id, timestamp) with
        {
            Status = ReadingStatus.InUseByAnotherApp,
            StatusDetail = "In use by another app",
        };

    private static BatteryReading New(SonyPadModel model, ConnectionType connection, string id, DateTimeOffset timestamp) => new()
    {
        DeviceId = id,
        Name = model.Name,
        Kind = DeviceKind.Gamepad,
        Connection = connection,
        Timestamp = timestamp,
        Source = SonyHidProvider.ProviderName,
    };

    private static string DescribeNoData(SonyReadResult read, ConnectionType connection, bool requestFullBluetoothReports)
    {
        if (read.CrcFailures > 0)
        {
            return "Bluetooth reports failed their checksum";
        }

        if (read.CalibrationError is not null)
        {
            return $"The pad refused the request for full Bluetooth reports ({read.CalibrationError})";
        }

        if (read.ReportsRead == 0)
        {
            return "The pad sent no reports";
        }

        return connection == ConnectionType.Bluetooth && !requestFullBluetoothReports
            ? "The pad is sending minimal Bluetooth reports, which carry no battery data"
            : "The pad sent only reports without battery data";
    }
}
