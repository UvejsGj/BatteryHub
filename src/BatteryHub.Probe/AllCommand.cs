using BatteryHub.Core;
using BatteryHub.Core.Ble;
using BatteryHub.Core.BluetoothProperty;
using BatteryHub.Core.Sony;
using BatteryHub.Core.XInput;

namespace BatteryHub.Probe;

/// <summary>Every reader once, then the merged list the tray app would show.</summary>
internal static class AllCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0)
        {
            throw new UsageException($"Unknown option '{args[0]}'.");
        }

        Program.PrintHeader();

        // Same readers and order as the app (the order settles ties between equally good readings).
        IBatteryProvider[] providers =
        [
            new SonyHidProvider(logger: new ProbeLogger<SonyHidProvider>()),
            new XInputProvider(new ProbeLogger<XInputProvider>()),
            new BleBatteryProvider(new ProbeLogger<BleBatteryProvider>()),
            new BluetoothPropertyProvider(),
        ];

        var byProvider = new List<IReadOnlyList<BatteryReading>>();
        try
        {
            foreach (var provider in providers)
            {
                Console.WriteLine();
                Console.WriteLine($"{provider.Name}:");
                try
                {
                    var readings = provider.PollAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Readings.Print(Console.Out, readings);
                    byProvider.Add(readings);
                }
                catch (Exception ex)
                {
                    // The app logs this and keeps the reader's previous readings.
                    Console.WriteLine($"  failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        finally
        {
            foreach (var provider in providers)
            {
                provider.Dispose();
            }
        }

        Console.WriteLine();
        Console.WriteLine("Merged (one row per device, as the tray shows them):");
        Readings.Print(Console.Out, BatteryMonitor.Combine(byProvider));
        return 0;
    }
}
