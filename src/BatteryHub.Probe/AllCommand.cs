using BatteryHub.Core;
using BatteryHub.Core.Ble;
using BatteryHub.Core.BluetoothProperty;
using BatteryHub.Core.Settings;
using BatteryHub.Core.Sony;
using BatteryHub.Core.XInput;

namespace BatteryHub.Probe;

/// <summary>Every reader once, then the merged list the tray app would show.</summary>
internal static class AllCommand
{
    public static int Run(string[] args)
    {
        bool noSwitch = false;
        foreach (string arg in args)
        {
            if (arg == "--no-switch")
            {
                noSwitch = true;
            }
            else
            {
                throw new UsageException($"Unknown option '{arg}'.");
            }
        }

        Program.PrintHeader();

        // Follow the tray's "Read Bluetooth PlayStation controllers" setting, so the list matches the tray.
        bool requestFullReports = !noSwitch
            && new SettingsStore(SettingsStore.DefaultDirectory, new ProbeLogger<SettingsStore>()).Load().ReadBluetoothPlayStationControllers;
        Console.WriteLine(requestFullReports
            ? "Bluetooth PlayStation pads: switched to full reports, as the tray setting says (--no-switch to skip)."
            : "Bluetooth PlayStation pads: left in their current report mode (tray setting off, or --no-switch).");

        // Same readers and order as the app (the order settles ties between equally good readings).
        IBatteryProvider[] providers =
        [
            new SonyHidProvider(new SonyHidOptions { RequestFullBluetoothReports = requestFullReports }, new ProbeLogger<SonyHidProvider>()),
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
