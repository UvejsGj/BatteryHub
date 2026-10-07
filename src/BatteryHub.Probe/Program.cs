using System.Reflection;
using System.Runtime.InteropServices;

namespace BatteryHub.Probe;

internal static class Program
{
    private const string Usage = """
        Usage: BatteryHub.Probe <command> [options]

        Commands:
          hid [--vid XXXX] [--pid XXXX]   List HID devices with VID, PID, top-level usage
                                          and report lengths. VID and PID are hex.
          sony [--no-switch]              Read every DualShock 4 and DualSense once: raw battery
                                          report, decoded reading, and a fixture block to paste.
                                          --no-switch leaves Bluetooth pads in their current
                                          report mode instead of requesting full reports.
          xinput                          Xbox controllers through XInput: raw battery type and
                                          level per slot, and the readings the app would show.
          ble                             Paired Bluetooth LE devices: connection state and the
                                          Battery Service read (connected devices only).
          bt [--all]                      The battery level Windows stores for Bluetooth headsets
                                          (hands-free) and LE devices, paired devices with their
                                          connection state, and the raw device nodes. --all dumps
                                          every Bluetooth device node.
          all                             Every reader once, then the merged list the tray shows.
        """;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (args[0] is "help" or "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            switch (args[0])
            {
                case "hid":
                    return HidCommand.Run(args[1..]);
                case "sony":
                    return SonyCommand.Run(args[1..]);
                case "xinput":
                    return XInputCommand.Run(args[1..]);
                case "ble":
                    return BleCommand.Run(args[1..]);
                case "bt":
                    return BluetoothCommand.Run(args[1..]);
                case "all":
                    return AllCommand.Run(args[1..]);
                default:
                    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    internal static string Version { get; } =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    internal static void PrintHeader()
    {
        Console.WriteLine($"BatteryHub.Probe {Version} | {RuntimeInformation.OSDescription} | {RuntimeInformation.FrameworkDescription}");
    }
}

internal sealed class UsageException(string message) : Exception(message);
