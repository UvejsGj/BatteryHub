namespace BatteryHub.Core.Sony;

public enum SonyReadOutcome
{
    /// <summary>A battery report arrived and was parsed.</summary>
    Battery,

    /// <summary>No report carrying battery data arrived in time.</summary>
    NoBatteryReport,

    /// <summary>A Sony wireless adapter with no pad attached.</summary>
    WirelessAdapterEmpty,
}

/// <summary>Everything one read of one pad saw, for the provider and for the Probe.</summary>
/// <param name="FirstRejectedReport">First report with the battery report's ID and length that failed its CRC, kept so the Probe can show it.</param>
public sealed record SonyReadResult(
    SonyReadOutcome Outcome,
    SonyBatteryStatus? Battery,
    byte[]? BatteryReport,
    byte[]? FirstRejectedReport,
    byte[]? FirstOtherReport,
    byte[]? CalibrationReport,
    string? CalibrationError,
    int ReportsRead,
    int OtherReports,
    int CrcFailures,
    TimeSpan Elapsed);

/// <summary>One open-read-close cycle against one pad.</summary>
internal static class SonyPadSession
{
    public static readonly TimeSpan DefaultReadWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Reads input reports until one carries battery data or <paramref name="window"/> passes. Over Bluetooth, when
    /// <paramref name="requestFullBluetoothReports"/> is set, first reads the calibration feature report: until that
    /// happens the pad sends a minimal report with no battery data.
    /// </summary>
    public static SonyReadResult Read(ISonyHidChannel channel, SonyPadModel model, ConnectionType connection, bool requestFullBluetoothReports, TimeProvider time, TimeSpan window)
    {
        byte[]? calibration = null;
        string? calibrationError = null;
        if (connection == ConnectionType.Bluetooth && requestFullBluetoothReports)
        {
            (calibration, calibrationError) = ReadCalibration(channel, model);
        }

        byte batteryId = model.BatteryReportId(connection);
        int batteryLength = model.BatteryReportLength(connection);
        var buffer = new byte[Math.Max(channel.MaxInputReportLength, batteryLength)];

        int reports = 0, others = 0, crcFailures = 0;
        byte[]? firstOther = null;
        byte[]? firstRejected = null;
        long start = time.GetTimestamp();
        while (true)
        {
            TimeSpan remaining = window - time.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            int read = channel.ReadInput(buffer, remaining);
            if (read <= 0)
            {
                break;
            }

            reports++;
            var report = buffer.AsSpan(0, read);
            if (report[0] == batteryId && read >= batteryLength)
            {
                if (model.IsWirelessAdapter && DualShock4Report.IsWirelessAdapterEmpty(report))
                {
                    return new(SonyReadOutcome.WirelessAdapterEmpty, null, null, firstRejected, null, calibration, calibrationError, reports, others, crcFailures, time.GetElapsedTime(start));
                }

                if (model.TryParse(report, connection, out var status))
                {
                    return new(SonyReadOutcome.Battery, status, report[..batteryLength].ToArray(), firstRejected, firstOther, calibration, calibrationError, reports, others, crcFailures, time.GetElapsedTime(start));
                }

                // Right ID and length but rejected: only the Bluetooth CRC check can do that.
                crcFailures++;
                firstRejected ??= report[..batteryLength].ToArray();
                continue;
            }

            others++;
            firstOther ??= report.ToArray();
        }

        return new(SonyReadOutcome.NoBatteryReport, null, null, firstRejected, firstOther, calibration, calibrationError, reports, others, crcFailures, time.GetElapsedTime(start));
    }

    private static (byte[]? Report, string? Error) ReadCalibration(ISonyHidChannel channel, SonyPadModel model)
    {
        var buffer = new byte[channel.MaxFeatureReportLength];
        if (buffer.Length == 0)
        {
            return (null, "the device declares no feature reports");
        }

        buffer[0] = model.BluetoothCalibrationReportId;
        try
        {
            channel.GetFeature(buffer);
            return (buffer, null);
        }
        catch (IOException ex)
        {
            return (null, ex.Message);
        }
    }
}
