using BatteryHub.Core;
using BatteryHub.Core.Ble;

namespace BatteryHub.Probe;

internal static class BleCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0)
        {
            throw new UsageException($"Unknown option '{args[0]}'.");
        }

        Program.PrintHeader();
        using var provider = new BleBatteryProvider();
        var devices = provider.InspectAsync(CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"Paired Bluetooth LE devices: {devices.Count}");
        foreach (var d in devices)
        {
            Console.WriteLine();
            Console.WriteLine($"{d.Device.Name}");
            Console.WriteLine($"   address    : {DeviceIds.Bluetooth(d.Device.Address)}");
            Console.WriteLine($"   connected  : {(d.Device.IsConnected ? "yes" : "no")}");
            Console.WriteLine($"   appearance : {(d.Device.Appearance is { } a ? $"0x{a:X4} (category 0x{a >> 6:X3}, sub 0x{a & 0x3F:X2}) -> {BleBattery.KindFromAppearance(a)}" : "none")}");
            if (d.Read is { } read)
            {
                Console.WriteLine($"   read       : {read.Status}{(read.Value is { } v ? $", value 0x{v:X2} ({v})" : "")}{(read.Detail is null ? "" : ", " + read.Detail)}");
            }

            if (d.Note is { } note)
            {
                Console.WriteLine($"   note       : {note}");
            }

            if (d.Reading is { } reading)
            {
                Console.WriteLine($"   reading    : {reading.Percent}%");
            }
        }

        return 0;
    }
}
