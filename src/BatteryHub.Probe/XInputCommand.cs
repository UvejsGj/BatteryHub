using BatteryHub.Core;
using BatteryHub.Core.Presentation;
using BatteryHub.Core.XInput;

namespace BatteryHub.Probe;

internal static class XInputCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0)
        {
            throw new UsageException($"Unknown option '{args[0]}'.");
        }

        Program.PrintHeader();
        var native = new XInputNative();
        try
        {
            for (int user = 0; user < XInputConstants.MaxUsers; user++)
            {
                var (result, battery) = XInputNative.QueryBattery(user);
                var hardware = native.GetHardware(user);
                string ids = hardware is { } h ? $"{h.VendorId:X4}:{h.ProductId:X4}{(h.IsCoveredElsewhere ? " (shown by another reader)" : "")}" : "unknown";
                Console.WriteLine(result == XInputConstants.ErrorSuccess
                    ? $"slot {user + 1}: type 0x{battery.Type:X2} ({TypeName(battery.Type)}), level 0x{battery.Level:X2}, hardware {ids}"
                    : $"slot {user + 1}: {(result == XInputConstants.ErrorDeviceNotConnected ? "no pad" : $"error {result}")}");
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Console.WriteLine($"XInput is not available: {ex.Message}");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Readings:");
        using var provider = new XInputProvider();
        var readings = provider.PollAsync(CancellationToken.None).GetAwaiter().GetResult();
        Readings.Print(Console.Out, readings);
        return 0;
    }

    private static string TypeName(byte type) => type switch
    {
        0x00 => "disconnected / no data yet",
        0x01 => "wired",
        0x02 => "alkaline",
        0x03 => "NiMH",
        0xFF => "unknown",
        _ => "?",
    };
}

/// <summary>Prints readings the way the tray app's list shows them.</summary>
internal static class Readings
{
    public static void Print(TextWriter output, IReadOnlyList<BatteryReading> readings)
    {
        if (readings.Count == 0)
        {
            output.WriteLine("  (none)");
            return;
        }

        foreach (var reading in readings)
        {
            var row = DeviceListPresenter.Row(reading);
            output.WriteLine($"  {row.Name}: {row.Level} | {row.Details}{(row.Problem is null ? "" : " | " + row.Problem)} | id {reading.DeviceId} | from {reading.Source}");
        }
    }
}
