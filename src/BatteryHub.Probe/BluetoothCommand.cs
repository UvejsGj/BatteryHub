using BatteryHub.Core;
using BatteryHub.Core.BluetoothProperty;
using BatteryHub.Core.Windows;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

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

    // As the readers request them, plus the address type, to see the raw values before BatteryHub parses them.
    private static readonly string[] EndpointProperties =
    [
        "System.Devices.Aep.DeviceAddress",
        "System.Devices.Aep.IsConnected",
        "System.Devices.Aep.ContainerId",
    ];

    private static readonly string[] LowEnergyEndpointProperties =
    [
        .. EndpointProperties,
        "System.Devices.Aep.Bluetooth.Le.Appearance",
        "System.Devices.Aep.Bluetooth.Le.AddressType",
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

        PrintRawEndpoints("classic", BluetoothDevice.GetDeviceSelectorFromPairingState(true), EndpointProperties);
        PrintRawEndpoints("LE", BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), LowEnergyEndpointProperties);

        IReadOnlyList<BluetoothEndpoint> endpoints;
        using (var source = new WindowsBluetoothPropertySource(new ProbeLogger<WindowsBluetoothPropertySource>()))
        {
            endpoints = source.ListPairedAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        Console.WriteLine();
        Console.WriteLine($"Paired Bluetooth devices as the reader parsed them: {endpoints.Count}");
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
            Console.WriteLine($"   device    : {(i.Endpoint is { } e ? $"{e.Name} ({(e.IsConnected ? "connected" : "disconnected")})" : "no paired device at this address" + ContainerMates(i.Node, endpoints))}");
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

    // Windows' paired-device records (association endpoints), exactly as WinRT returns them. Reads no device.
    private static void PrintRawEndpoints(string label, string selector, string[] properties)
    {
        Console.WriteLine();
        Console.WriteLine($"Paired {label} endpoints (no scan, no connection). Query: {selector}");
        var found = DeviceInformation.FindAllAsync(selector, properties, DeviceInformationKind.AssociationEndpoint).AsTask().GetAwaiter().GetResult();
        foreach (var info in found)
        {
            Console.WriteLine($"  {info.Name}");
            Console.WriteLine($"     id: {info.Id}");
            foreach (string property in properties)
            {
                string value = info.Properties.TryGetValue(property, out object? raw)
                    ? raw is null ? "null" : $"{raw} ({raw.GetType().Name})"
                    : "absent";
                Console.WriteLine($"     {property["System.Devices.Aep.".Length..]}: {value}");
            }
        }

        if (found.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
    }

    // Not used to match (another node's connection state says nothing about this one), but worth seeing on hardware.
    private static string ContainerMates(BluetoothBatteryNode node, IReadOnlyList<BluetoothEndpoint> endpoints)
    {
        var mates = endpoints.Where(e => node.ContainerId is { } id && e.ContainerId == id).ToList();
        return mates.Count == 0
            ? ""
            : "; same container as " + string.Join(", ", mates.Select(e => $"{e.Name} {DeviceIds.Bluetooth(e.Address)} ({(e.IsLowEnergy ? "LE" : "classic")}, {(e.IsConnected ? "connected" : "disconnected")})"));
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
