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

    internal static void PrintHeader()
    {
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Console.WriteLine($"BatteryHub.Probe {version} | {RuntimeInformation.OSDescription} | {RuntimeInformation.FrameworkDescription}");
    }
}

internal sealed class UsageException(string message) : Exception(message);
