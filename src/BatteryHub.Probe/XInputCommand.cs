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
        try
        {
            Console.WriteLine($"XInputGetCapabilitiesEx (ordinal 108): {(XInputNative.CapabilitiesExAvailable ? "found" : "not exported; Bluetooth and Steam pads cannot be told apart")}");
            for (int user = 0; user < XInputConstants.MaxUsers; user++)
            {
                var (result, battery) = XInputNative.QueryBattery(user);
                Console.WriteLine(result == XInputConstants.ErrorSuccess
                    ? $"slot {user + 1}: battery type 0x{battery.Type:X2} ({TypeName(battery.Type)}), level 0x{battery.Level:X2}"
                    : $"slot {user + 1}: battery {(result == XInputConstants.ErrorDeviceNotConnected ? "no pad" : $"error {result}")}");
                if (XInputNative.QueryCapabilities(user) is { } caps)
                {
                    var hardware = new XInputHardware(caps.VendorId, caps.ProductId);
                    Console.WriteLine(caps.Result == XInputConstants.ErrorSuccess
                        ? $"        capabilities: {caps.VendorId:X4}:{caps.ProductId:X4} version {caps.Version:X4}, type 0x{caps.Type:X2}, subtype 0x{caps.SubType:X2}, flags 0x{caps.Flags:X4}{((caps.Flags & XInputConstants.CapsWireless) != 0 ? " (wireless)" : "")}{HardwareNote(hardware)}"
                        : $"        capabilities: {(caps.Result == XInputConstants.ErrorDeviceNotConnected ? "no pad" : $"error {caps.Result}")}");
                }
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
        if (HoldsEmpty(readings))
        {
            Console.WriteLine();
            Console.WriteLine($"Readings {ConfirmWait.TotalSeconds:0} s later (what the app shows once a first \"empty\" holds):");
            Readings.Print(Console.Out, PollAgain(provider));
        }

        return 0;
    }

    // A first EMPTY is held back until it lasts EmptyConfirmDelay; the app's next poll (30 s later) shows it.
    internal static readonly TimeSpan ConfirmWait = XInputConstants.EmptyConfirmDelay + TimeSpan.FromSeconds(1);

    internal static bool HoldsEmpty(IReadOnlyList<BatteryReading> readings) =>
        readings.Any(r => r.StatusDetail == XInputProvider.EmptyHeldDetail);

    internal static IReadOnlyList<BatteryReading> PollAgain(XInputProvider provider)
    {
        Thread.Sleep(ConfirmWait);
        return provider.PollAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string HardwareNote(XInputHardware hardware) =>
        hardware.IsCoveredElsewhere ? " (skipped: Bluetooth LE pad or Steam's virtual pad, shown by other readers)"
        : hardware.IsClassicBluetooth ? " (classic Bluetooth: shown as no data)"
        : "";

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
