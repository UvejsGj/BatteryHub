namespace BatteryHub.Core.Sony;

/// <summary>Works out a pad's <see cref="BatteryReading.DeviceId"/> on every poll. Not thread-safe; the provider serialises polls.</summary>
/// <remarks>
/// The pad is asked every time: the pad behind a device path can change without the path changing (another pad in
/// the same USB port, or the wireless adapter paired to a different pad). The last ID read at a path is kept only as
/// a fallback for when the pad cannot be asked, and only until the device list changes.
/// </remarks>
internal sealed class SonyIdResolver
{
    private readonly Dictionary<string, (string Id, string Source)> _lastIds = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;

    /// <summary>Forgets every remembered ID if the device list changed since the last poll.</summary>
    public void BeginPoll(int deviceListGeneration)
    {
        if (deviceListGeneration != _generation)
        {
            _lastIds.Clear();
            _generation = deviceListGeneration;
        }
    }

    /// <summary>For a pad that is open.</summary>
    /// <param name="serial">The HID serial number string, or null if it could not be read.</param>
    /// <param name="mayReadBluetoothFeatures">
    /// False when Bluetooth pads must be left in their report mode: any feature read can switch a Bluetooth pad to full
    /// reports (SDL notes this for the DualSense pairing report), so only the serial number is used then.
    /// </param>
    public (string Id, string Source) Resolve(string path, SonyPadModel model, ConnectionType connection, string? serial, ISonyHidChannel channel, bool mayReadBluetoothFeatures)
    {
        // Over Bluetooth, Windows reports the pad's address as the HID serial number, which costs no device I/O.
        // Over USB the serial is not the MAC, so ask the pad (DS4Windows does the same).
        string mac, source;
        bool gotMac = connection == ConnectionType.Bluetooth
            ? TryParseSerial(serial, out mac, out source) || (mayReadBluetoothFeatures && TryReadPairingMac(channel, model, connection, out mac, out source))
            : TryReadPairingMac(channel, model, connection, out mac, out source) || TryParseSerial(serial, out mac, out source);
        if (!gotMac)
        {
            return LastOrPathId(path, model);
        }

        var found = (SonyIdentity.FromMac(mac), source);
        if (!model.IsWirelessAdapter)
        {
            _lastIds[path] = found;
        }

        return found;
    }

    /// <summary>
    /// For a pad that cannot be opened. The serial number needs no access rights, so it still works while another
    /// app holds the pad exclusively.
    /// </summary>
    public (string Id, string Source) ResolveWithoutOpening(string path, SonyPadModel model, ConnectionType connection, string? serial) =>
        connection == ConnectionType.Bluetooth && TryParseSerial(serial, out string mac, out string source)
            ? (SonyIdentity.FromMac(mac), source)
            : LastOrPathId(path, model);

    private (string Id, string Source) LastOrPathId(string path, SonyPadModel model) =>
        !model.IsWirelessAdapter && _lastIds.TryGetValue(path, out var last)
            ? (last.Id, $"{last.Source}, earlier read")
            : (SonyIdentity.FromDevicePath(path), "device path");

    private static bool TryParseSerial(string? serial, out string mac, out string source)
    {
        source = "serial number";
        return SonyIdentity.TryParseSerial(serial, out mac);
    }

    private static bool TryReadPairingMac(ISonyHidChannel channel, SonyPadModel model, ConnectionType connection, out string mac, out string source)
    {
        mac = "";
        source = "";
        (byte id, int length, bool crc) = (model.Family, connection) switch
        {
            (SonyPadFamily.DualSense, _) => (DualSenseConstants.PairingInfoFeatureReportId, DualSenseConstants.PairingInfoFeatureReportLength, connection == ConnectionType.Bluetooth),
            (SonyPadFamily.DualShock4, ConnectionType.Usb) => (DualShock4Constants.UsbPairingInfoFeatureReportId, DualShock4Constants.UsbPairingInfoFeatureReportLength, false),
            _ => ((byte)0, 0, false),
        };
        if (length == 0)
        {
            return false;
        }

        var buffer = new byte[Math.Max(channel.MaxFeatureReportLength, length)];
        buffer[0] = id;
        try
        {
            channel.GetFeature(buffer);
        }
        catch (IOException)
        {
            return false;
        }

        source = $"pairing report 0x{id:X2}";
        return SonyIdentity.TryParsePairingReport(buffer, id, length, crc, out mac);
    }
}
