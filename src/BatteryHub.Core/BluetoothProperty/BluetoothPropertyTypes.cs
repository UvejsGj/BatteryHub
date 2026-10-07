namespace BatteryHub.Core.BluetoothProperty;

public enum BluetoothNodeKind
{
    /// <summary>A classic headset's hands-free (HFP) node.</summary>
    HandsFree,

    /// <summary>A Bluetooth LE device node.</summary>
    LowEnergy,
}

/// <summary>A device node that can hold Windows' battery value for a Bluetooth device.</summary>
/// <param name="Percent">The stored value when it is a valid percentage; null when unset or malformed.</param>
/// <param name="BatteryRaw">The stored value as Windows returned it, for the Probe.</param>
/// <param name="ContainerId">Groups the nodes and endpoints of one physical device.</param>
public sealed record BluetoothBatteryNode(
    string InstanceId,
    BluetoothNodeKind Kind,
    ulong Address,
    string? FriendlyName,
    Guid? ContainerId,
    int? Percent,
    string BatteryRaw);

/// <summary>A paired Bluetooth device as Windows lists it, with its current connection state.</summary>
public sealed record BluetoothEndpoint(
    ulong Address,
    bool IsLowEnergy,
    string Name,
    bool IsConnected,
    Guid? ContainerId,
    ushort? Appearance);

/// <summary>The Windows calls the reader needs, abstracted so its logic can be tested.</summary>
internal interface IBluetoothPropertySource
{
    /// <summary>Present hands-free and LE device nodes with the battery value each holds.</summary>
    IReadOnlyList<BluetoothBatteryNode> ListNodes();

    /// <summary>Paired classic and LE devices and whether each is connected now. Neither scans nor connects.</summary>
    Task<IReadOnlyList<BluetoothEndpoint>> ListPairedAsync(CancellationToken cancellationToken);
}
