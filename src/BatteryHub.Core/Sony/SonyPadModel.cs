namespace BatteryHub.Core.Sony;

public enum SonyPadFamily
{
    DualShock4,
    DualSense,
}

/// <summary>A Sony product ID this provider reads, and how to name it.</summary>
public sealed record SonyPadModel(ushort ProductId, string Name, SonyPadFamily Family, bool IsWirelessAdapter = false)
{
    public static IReadOnlyList<SonyPadModel> All { get; } =
    [
        new(DualShock4Constants.ProductIdV1, "DualShock 4", SonyPadFamily.DualShock4),
        new(DualShock4Constants.ProductIdV2, "DualShock 4", SonyPadFamily.DualShock4),
        new(DualShock4Constants.WirelessAdapterProductId, "DualShock 4 (wireless adapter)", SonyPadFamily.DualShock4, IsWirelessAdapter: true),
        new(DualSenseConstants.ProductId, "DualSense", SonyPadFamily.DualSense),
        new(DualSenseConstants.EdgeProductId, "DualSense Edge", SonyPadFamily.DualSense),
    ];

    public static SonyPadModel? Find(int vendorId, int productId) =>
        vendorId == SonyProtocol.VendorId ? All.FirstOrDefault(m => m.ProductId == productId) : null;

    /// <summary>Report ID that carries battery data on this transport.</summary>
    public byte BatteryReportId(ConnectionType connection) => (Family, connection) switch
    {
        (SonyPadFamily.DualShock4, ConnectionType.Bluetooth) => DualShock4Constants.BluetoothInputReportId,
        (SonyPadFamily.DualShock4, _) => DualShock4Constants.UsbInputReportId,
        (SonyPadFamily.DualSense, ConnectionType.Bluetooth) => DualSenseConstants.BluetoothInputReportId,
        (SonyPadFamily.DualSense, _) => DualSenseConstants.UsbInputReportId,
        _ => throw new InvalidOperationException($"Unknown family {Family}."),
    };

    /// <summary>Length of that report, CRC included.</summary>
    public int BatteryReportLength(ConnectionType connection) => (Family, connection) switch
    {
        (SonyPadFamily.DualShock4, ConnectionType.Bluetooth) => DualShock4Constants.BluetoothInputReportLength,
        (SonyPadFamily.DualShock4, _) => DualShock4Constants.UsbInputReportLength,
        (SonyPadFamily.DualSense, ConnectionType.Bluetooth) => DualSenseConstants.BluetoothInputReportLength,
        (SonyPadFamily.DualSense, _) => DualSenseConstants.UsbInputReportLength,
        _ => throw new InvalidOperationException($"Unknown family {Family}."),
    };

    /// <summary>Index of the battery status byte in that report.</summary>
    public int BatteryByteOffset(ConnectionType connection) => (Family, connection) switch
    {
        (SonyPadFamily.DualShock4, ConnectionType.Bluetooth) => DualShock4Constants.BluetoothStatusOffset,
        (SonyPadFamily.DualShock4, _) => DualShock4Constants.UsbStatusOffset,
        (SonyPadFamily.DualSense, ConnectionType.Bluetooth) => DualSenseConstants.BluetoothStatusOffset,
        (SonyPadFamily.DualSense, _) => DualSenseConstants.UsbStatusOffset,
        _ => throw new InvalidOperationException($"Unknown family {Family}."),
    };

    /// <summary>Parses a battery report for this pad. Bluetooth reports must pass their CRC.</summary>
    public bool TryParse(ReadOnlySpan<byte> report, ConnectionType connection, out SonyBatteryStatus status) => (Family, connection) switch
    {
        (SonyPadFamily.DualShock4, ConnectionType.Bluetooth) => DualShock4Report.TryParseBluetooth(report, out status),
        (SonyPadFamily.DualShock4, _) => DualShock4Report.TryParseUsb(report, out status),
        (SonyPadFamily.DualSense, ConnectionType.Bluetooth) => DualSenseReport.TryParseBluetooth(report, out status),
        (SonyPadFamily.DualSense, _) => DualSenseReport.TryParseUsb(report, out status),
        _ => throw new InvalidOperationException($"Unknown family {Family}."),
    };

    /// <summary>Feature report that switches a Bluetooth pad from its minimal report to the full one.</summary>
    public byte BluetoothCalibrationReportId => Family == SonyPadFamily.DualShock4
        ? DualShock4Constants.BluetoothCalibrationFeatureReportId
        : DualSenseConstants.CalibrationFeatureReportId;
}
