using System.Buffers.Binary;
using System.Globalization;
using BatteryHub.Core;
using BatteryHub.Core.Sony;

namespace BatteryHub.Probe;

internal static class SonyCommand
{
    public static int Run(string[] args)
    {
        bool requestFullReports = true;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--no-switch":
                    requestFullReports = false;
                    break;
                default:
                    throw new UsageException($"Unknown option '{arg}'.");
            }
        }

        Program.PrintHeader();
        using var provider = new SonyHidProvider(new SonyHidOptions { RequestFullBluetoothReports = requestFullReports });
        var pads = provider.InspectAsync(CancellationToken.None).GetAwaiter().GetResult();

        Console.WriteLine($"Sony pads: {pads.Count}");
        Console.WriteLine(requestFullReports
            ? "Bluetooth pads: feature report 0x05 requested to switch them to full reports (--no-switch to skip)."
            : "Bluetooth pads: left in whatever report mode they are in (--no-switch).");
        if (pads.Count == 0)
        {
            Console.WriteLine("No DualShock 4 or DualSense found. Plug one in or connect it over Bluetooth, then run again.");
        }

        for (int i = 0; i < pads.Count; i++)
        {
            Console.WriteLine();
            Print(Console.Out, i + 1, pads[i]);
        }

        return 0;
    }

    internal static void Print(TextWriter output, int index, SonyDeviceInspection pad)
    {
        output.WriteLine($"#{index} {pad.Model.Name} 054C:{pad.Model.ProductId:X4}  {pad.Connection}");
        if (pad.IsVirtual)
        {
            output.WriteLine("   skipped      : virtual pad created by software (e.g. DS4Windows through ViGEm)");
            output.WriteLine($"   path         : {pad.DevicePath}");
            return;
        }

        output.WriteLine($"   device id    : {pad.DeviceId} (from {pad.IdSource})");
        if (pad.Read is { } read)
        {
            output.WriteLine($"   calibration  : {DescribeCalibration(read, pad.Connection)}");
            output.WriteLine($"   reports      : {read.ReportsRead} read in {read.Elapsed.TotalMilliseconds:0} ms, {read.OtherReports} without battery data, {read.CrcFailures} failed CRC");
            if (read.FirstOtherReport is { } other)
            {
                output.WriteLine($"   first other  : {Hex.Trimmed(other)}");
            }

            if (read.FirstRejectedReport is { } rejected)
            {
                output.WriteLine($"   failed CRC   : stored 0x{BinaryPrimitives.ReadUInt32LittleEndian(rejected.AsSpan(rejected.Length - 4)):X8}, computed 0x{SonyCrc.Compute(SonyProtocol.InputCrcSeed, rejected.AsSpan(0, rejected.Length - 4)):X8} (seed 0xA1)");
                foreach (string line in Hex.Lines(rejected))
                {
                    output.WriteLine($"     {line}");
                }
            }

            if (read.Battery is { } battery && read.BatteryReport is { } report)
            {
                int offset = pad.Model.BatteryByteOffset(pad.Connection);
                output.WriteLine($"   battery byte : [{offset}] = 0x{battery.StatusByte:X2} -> {DescribeStatusByte(pad.Model, battery)}");
            }
        }

        output.WriteLine($"   reading      : {DescribeReading(pad)}");
        if (pad.Read?.BatteryReport is { } raw && pad.Reading is { } reading)
        {
            output.WriteLine("   fixture      :");
            foreach (string line in FixtureLines(pad, reading, raw))
            {
                output.WriteLine($"     {line}");
            }
        }

        output.WriteLine($"   path         : {pad.DevicePath}");
    }

    private static string DescribeCalibration(SonyReadResult read, ConnectionType connection)
    {
        if (read.CalibrationError is { } error)
        {
            return $"request for 0x05 failed: {error}";
        }

        if (read.CalibrationReport is { } report)
        {
            return $"read 0x{report[0]:X2} ({report.Length}-byte buffer)";
        }

        return connection == ConnectionType.Bluetooth ? "not requested" : "not needed over USB";
    }

    private static string DescribeStatusByte(SonyPadModel model, SonyBatteryStatus battery) => model.Family == SonyPadFamily.DualShock4
        ? $"level {battery.Level}, cable {(battery.CableConnected ? "in" : "out")}"
        : $"level {battery.Level}, state 0x{battery.StatusByte >> 4:X}";

    private static string DescribeReading(SonyDeviceInspection pad)
    {
        if (pad.Reading is not { } reading)
        {
            return pad.Error is { } error
                ? $"none ({error})"
                : pad.Read?.Outcome == SonyReadOutcome.WirelessAdapterEmpty ? "none (no pad attached to the adapter)" : "none";
        }

        return reading.Status switch
        {
            ReadingStatus.Ok => $"{reading.Percent}% {reading.ChargeState}",
            ReadingStatus.InUseByAnotherApp => $"in use by another app ({pad.Error})",
            _ => $"{reading.Status}: {reading.StatusDetail}",
        };
    }

    private static IEnumerable<string> FixtureLines(SonyDeviceInspection pad, BatteryReading reading, byte[] report)
    {
        string version = Program.Version;
        yield return $"# pad: {pad.Model.Name}";
        yield return $"# product-id: {pad.Model.ProductId:X4}";
        yield return $"# connection: {pad.Connection}";
        yield return $"# source: captured with BatteryHub.Probe {version} on {DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        yield return $"# expect: {(reading.Status == ReadingStatus.DeviceError ? "fault" : $"{reading.Percent}% {reading.ChargeState}")}";
        foreach (string line in Hex.Lines(report))
        {
            yield return line;
        }
    }
}
