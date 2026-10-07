using System.Globalization;
using BatteryHub.Core;
using BatteryHub.Core.Hid;

namespace BatteryHub.Probe;

internal static class HidCommand
{
    public static int Run(string[] args)
    {
        int? vid = null;
        int? pid = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vid":
                    vid = ParseHexId(args, ++i, "--vid");
                    break;
                case "--pid":
                    pid = ParseHexId(args, ++i, "--pid");
                    break;
                default:
                    throw new UsageException($"Unknown option '{args[i]}'.");
            }
        }

        Program.PrintHeader();
        var all = HidInventory.Enumerate();
        var shown = all
            .Where(d => (vid is null || d.VendorId == vid) && (pid is null || d.ProductId == pid))
            .ToList();

        string filter = vid is null && pid is null
            ? ""
            : $", {shown.Count} matching{(vid is null ? "" : $" vid={vid:X4}")}{(pid is null ? "" : $" pid={pid:X4}")}";
        Console.WriteLine($"HID devices: {all.Count}{filter}");
        Console.WriteLine("Lengths are in bytes and count the report ID byte.");

        for (int i = 0; i < shown.Count; i++)
        {
            Console.WriteLine();
            Print(i + 1, shown[i]);
        }

        return 0;
    }

    private static void Print(int index, HidDeviceSummary d)
    {
        string names = string.Join(" / ", new[] { d.ProductName, d.Manufacturer }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Console.WriteLine($"#{index} {d.VendorId:X4}:{d.ProductId:X4} rev {d.ReleaseNumberBcd:X4}  {(names.Length > 0 ? names : "(no name)")}");
        Console.WriteLine($"   connection : {Describe(d.ConnectionGuess)} (guessed from path)");

        if (d.Layout is { } layout)
        {
            Console.WriteLine($"   usage      : {DescribeUsage(layout.TopLevelUsage)}");
        }

        Console.WriteLine($"   max length : input {Length(d.MaxInputReportLength)}, output {Length(d.MaxOutputReportLength)}, feature {Length(d.MaxFeatureReportLength)}");

        if (d.Layout is { } reports)
        {
            Console.WriteLine($"   input      : {ReportList(reports, HidReportKind.Input)}");
            Console.WriteLine($"   output     : {ReportList(reports, HidReportKind.Output)}");
            Console.WriteLine($"   feature    : {ReportList(reports, HidReportKind.Feature)}");
        }
        else
        {
            Console.WriteLine($"   reports    : descriptor unavailable ({d.LayoutError})");
        }

        Console.WriteLine($"   path       : {d.DevicePath}");
    }

    private static int ParseHexId(string[] args, int index, string option)
    {
        if (index >= args.Length)
        {
            throw new UsageException($"{option} needs a hex value, e.g. {option} 054C.");
        }

        string text = args[index];
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (!int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value) || value > 0xFFFF)
        {
            throw new UsageException($"{option} '{args[index]}' is not a 16-bit hex value.");
        }

        return value;
    }

    private static string Describe(ConnectionType connection) => connection switch
    {
        ConnectionType.Usb => "USB",
        ConnectionType.Bluetooth => "Bluetooth",
        ConnectionType.Ble => "Bluetooth LE",
        _ => "unknown",
    };

    private static string Length(int? length) => length?.ToString(CultureInfo.InvariantCulture) ?? "?";

    private static string ReportList(HidReportLayout layout, HidReportKind kind)
    {
        var reports = layout.Reports.Where(r => r.Kind == kind).Select(r => $"0x{r.ReportId:X2}({r.Length})").ToList();
        return reports.Count > 0 ? string.Join(" ", reports) : "-";
    }

    private static string DescribeUsage(HidUsage? usage)
    {
        if (usage is not { } u)
        {
            return "?";
        }

        string? name = (u.Page, u.Id) switch
        {
            (0x01, 0x01) => "Generic Desktop / Pointer",
            (0x01, 0x02) => "Generic Desktop / Mouse",
            (0x01, 0x04) => "Generic Desktop / Joystick",
            (0x01, 0x05) => "Generic Desktop / Game Pad",
            (0x01, 0x06) => "Generic Desktop / Keyboard",
            (0x01, 0x07) => "Generic Desktop / Keypad",
            (0x01, 0x08) => "Generic Desktop / Multi-axis Controller",
            (0x01, 0x80) => "Generic Desktop / System Control",
            (0x0B, 0x05) => "Telephony / Headset",
            (0x0C, 0x01) => "Consumer / Consumer Control",
            ( >= 0xFF00, _) => "Vendor-defined",
            _ => null,
        };
        return name is null ? u.ToString() : $"{u} {name}";
    }
}
