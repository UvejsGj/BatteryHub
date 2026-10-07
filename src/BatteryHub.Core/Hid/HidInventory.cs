using HidSharp;

namespace BatteryHub.Core.Hid;

/// <summary>What the Probe reports about one HID top-level collection.</summary>
/// <param name="MaxInputReportLength">Largest input report in bytes, counting the report ID byte. Null if it could not be read.</param>
/// <param name="Layout">Reports declared by the descriptor. Null if it could not be read; see <paramref name="LayoutError"/>.</param>
public sealed record HidDeviceSummary(
    int VendorId,
    int ProductId,
    int ReleaseNumberBcd,
    string DevicePath,
    ConnectionType ConnectionGuess,
    string? Manufacturer,
    string? ProductName,
    int? MaxInputReportLength,
    int? MaxOutputReportLength,
    int? MaxFeatureReportLength,
    HidReportLayout? Layout,
    string? LayoutError);

/// <summary>Lists HID devices without opening them for reading or writing.</summary>
public static class HidInventory
{
    public static IReadOnlyList<HidDeviceSummary> Enumerate() =>
        DeviceList.Local.GetHidDevices()
            .Select(Describe)
            .OrderBy(d => d.VendorId)
            .ThenBy(d => d.ProductId)
            .ThenBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static HidDeviceSummary Describe(HidDevice device)
    {
        HidReportLayout? layout = null;
        string? layoutError = null;
        try
        {
            layout = HidReportLayout.From(device.GetReportDescriptor());
        }
        catch (Exception ex)
        {
            layoutError = $"{ex.GetType().Name}: {ex.Message}";
        }

        return new HidDeviceSummary(
            device.VendorID,
            device.ProductID,
            device.ReleaseNumberBcd,
            device.DevicePath,
            HidDevicePath.GuessConnection(device.DevicePath),
            TryRead(device.GetManufacturer),
            TryRead(device.GetProductName),
            TryRead(device.GetMaxInputReportLength),
            TryRead(device.GetMaxOutputReportLength),
            TryRead(device.GetMaxFeatureReportLength),
            layout,
            layoutError);
    }

    // String and length queries can fail per device (unplugged mid-enumeration, driver quirks).
    // A missing value is shown as unknown rather than dropping the device from the list.
    private static string? TryRead(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int? TryRead(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
