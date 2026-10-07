using BatteryHub.Core.Windows;

namespace BatteryHub.Core.BluetoothProperty;

/// <summary>Where Windows stores the battery level it shows in Settings for Bluetooth devices.</summary>
internal static class BluetoothPropertyConstants
{
    /// <summary>
    /// Battery level, one DEVPROP_TYPE_BYTE, 0-100. Not in any SDK or WDK header; community readers call it
    /// DEVPKEY_Bluetooth_Battery (Windhawk, RDevora47/battery-buddy, SARDONYX-sard/bluetooth-battery-monitor,
    /// Jccqt/BattTray, fastfetch). Windows fills it from the HFP battery indicator on a classic headset's hands-free
    /// node and from the GATT Battery Service on an LE device's node, and keeps the last value after a disconnect.
    /// </summary>
    public static readonly DevPropKey Battery = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);

    /// <summary>Same undocumented set as <see cref="Battery"/>; meaning unconfirmed (named BatteryLow by some tools). Probe only.</summary>
    public static readonly DevPropKey BatterySibling = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 3);

    /// <summary>DEVPKEY_Bluetooth_DeviceAddress (WDK bthguid.h): 12 hex digits, no separators, on BR/EDR and LE nodes.</summary>
    public static readonly DevPropKey DeviceAddress = new(new Guid("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"), 1);

    /// <summary>DEVPKEY_Bluetooth_DeviceFlags (WDK bthguid.h): BDIF_* bits from bthdef.h. Probe only.</summary>
    public static readonly DevPropKey DeviceFlags = new(new Guid("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"), 3);

    /// <summary>Undocumented; scripts use it as "is connected" on a device node. Probe only, to compare with the documented state.</summary>
    public static readonly DevPropKey UndocumentedConnected = new(new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), 15);

    // bthdef.h BDIF_* bits, for the Probe.
    public const uint FlagConnected = 0x0000_0020;
    public const uint FlagLeConnected = 0x0100_0000;

    /// <summary>Classic hands-free nodes: service class 0x111E (Handsfree), which headsets publish.</summary>
    /// <remarks>
    /// 0x111F (HandsfreeAudioGateway) nodes are phones and tablets paired to the PC, not headsets. The node's setup
    /// class is System, not Bluetooth, so it is found by instance ID, not by class.
    /// </remarks>
    public const string HandsFreePrefix = @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}";

    /// <summary>Bluetooth LE device nodes: BTHLE\DEV_&lt;address&gt;\...</summary>
    public const string LowEnergyPrefix = @"BTHLE\DEV_";

    /// <summary>Classic service nodes end with "&amp;&lt;address&gt;_C00000000".</summary>
    public const string ClassicServiceSuffix = "_C00000000";

    public const string ClassicEnumerator = "BTHENUM";
    public const string LowEnergyEnumerator = "BTHLE";
}
