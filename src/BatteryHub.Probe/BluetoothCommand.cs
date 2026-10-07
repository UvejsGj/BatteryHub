using BatteryHub.Core;
using BatteryHub.Core.BluetoothProperty;
using BatteryHub.Core.Windows;

namespace BatteryHub.Probe;

/// <summary>The battery level Windows stores for Bluetooth devices, plus the raw device nodes around it.</summary>
internal static class BluetoothCommand
{
    // HFP audio nodes (BTHHFENUM) and GATT service nodes (BTHLEDEVICE) are dumped to see whether Windows also stores
    // the level there.
    private static readonly string[] Enumerators = ["BTHENUM", "BTHLE", "BTHLEDEVICE", "BTHHFENUM"];

    private static readonly (string Label, DevPropKey Key)[] RawKeys =
    [
        ("name", DeviceNodes.FriendlyName),
        ("description", DeviceNodes.DeviceDescription),
        ("class", DeviceNodes.ClassGuid),
        ("parent", DeviceNodes.Parent),
        ("container", DeviceNodes.ContainerId),
        ("address", BluetoothPropertyConstants.DeviceAddress),
        ("flags", BluetoothPropertyConstants.DeviceFlags),
        ("battery {..} 2", BluetoothPropertyConstants.Battery),
        ("battery {..} 3", BluetoothPropertyConstants.BatterySibling),
        ("{83DA6326} 15", BluetoothPropertyConstants.UndocumentedConnected),
    ];

    public static int Run(string[] args)
    {
        bool all = false;
        foreach (string arg in args)
        {
            if (arg == "--all")
            {
                all = true;
            }
            else
            {
                throw new UsageException($"Unknown option '{arg}'.");
            }
        }

        Program.PrintHeader();

        var endpoints = new WindowsBluetoothPropertySource().ListPairedAsync(CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"Paired Bluetooth devices (Windows' records; no scan, no connection): {endpoints.Count}");
        foreach (var e in endpoints)
        {
            Console.WriteLine($"  {DeviceIds.Bluetooth(e.Address)}  {(e.IsLowEnergy ? "LE     " : "classic")}  {(e.IsConnected ? "connected   " : "disconnected")}  container {e.ContainerId?.ToString() ?? "-"}  {e.Name}");
        }

        using var provider = new BluetoothPropertyProvider();
        var inspections = provider.InspectAsync(CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine();
        Console.WriteLine($"Hands-free and LE nodes: {inspections.Count}");
        foreach (var i in inspections)
        {
            Console.WriteLine();
            Console.WriteLine($"{i.Node.InstanceId}");
            Console.WriteLine($"   kind      : {i.Node.Kind}");
            Console.WriteLine($"   address   : {DeviceIds.Bluetooth(i.Node.Address)}");
            Console.WriteLine($"   name      : {i.Node.FriendlyName ?? "-"}");
            Console.WriteLine($"   container : {i.Node.ContainerId?.ToString() ?? "-"}");
            Console.WriteLine($"   stored    : {i.Node.BatteryRaw}{(i.Node.Percent is { } p ? $" -> {p}%" : "")}");
            Console.WriteLine($"   device    : {(i.Endpoint is { } e ? $"{e.Name} ({(e.IsConnected ? "connected" : "disconnected")})" : "no paired device matched")}");
            if (i.Note is { } note)
            {
                Console.WriteLine($"   note      : {note}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(all
            ? "Raw Bluetooth device nodes (all):"
            : "Raw Bluetooth device nodes (device, hands-free, LE, HFP audio, and any node with a stored level; --all for every node):");
        foreach (string enumerator in Enumerators)
        {
            foreach (string instanceId in DeviceNodes.ListPresent(enumerator))
            {
                if (!DeviceNodes.TryLocate(instanceId, out uint node))
                {
                    continue;
                }

                var values = RawKeys.Select(k => (k.Label, Value: DeviceNodes.Read(node, k.Key))).ToList();
                bool hasLevel = values.Any(v => v.Label.StartsWith("battery", StringComparison.Ordinal) && v.Value.Found);
                if (!all && !hasLevel && !IsInteresting(instanceId))
                {
                    continue;
                }

                Console.WriteLine();
                Console.WriteLine(instanceId);
                foreach (var (label, value) in values.Where(v => v.Value.Found || v.Label.StartsWith("battery", StringComparison.Ordinal)))
                {
                    Console.WriteLine($"   {label,-15}: {value.Describe()}{FlagsNote(label, value)}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("Readings the app would show:");
        Readings.Print(Console.Out, provider.PollAsync(CancellationToken.None).GetAwaiter().GetResult());
        return 0;
    }

    private static bool IsInteresting(string instanceId) =>
        BluetoothNodeId.Classify(instanceId) is not null
        || instanceId.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase)
        || instanceId.StartsWith("BTHHFENUM", StringComparison.OrdinalIgnoreCase);

    private static string FlagsNote(string label, DeviceProperty value)
    {
        if (label != "flags" || value is not { Type: DeviceProperty.TypeUInt32, Data.Length: 4 })
        {
            return "";
        }

        uint flags = BitConverter.ToUInt32(value.Data);
        return $" (connected {((flags & BluetoothPropertyConstants.FlagConnected) != 0 ? "yes" : "no")}, LE connected {((flags & BluetoothPropertyConstants.FlagLeConnected) != 0 ? "yes" : "no")})";
    }
}
