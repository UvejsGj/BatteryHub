using HidSharp.Reports;

namespace BatteryHub.Core.Hid;

public enum HidReportKind
{
    Input,
    Output,
    Feature,
}

/// <summary>A HID usage: page in the high 16 bits, ID in the low 16 bits.</summary>
public readonly record struct HidUsage(ushort Page, ushort Id)
{
    public static HidUsage FromExtended(uint usage) => new((ushort)(usage >> 16), (ushort)usage);

    public override string ToString() => $"0x{Page:X4}:0x{Id:X4}";
}

/// <summary>One report declared by a report descriptor.</summary>
/// <param name="Length">Report length in bytes, counting the report ID byte at index 0.</param>
public readonly record struct HidReportInfo(HidReportKind Kind, byte ReportId, int Length);

/// <summary>The top-level usage and the reports declared by one HID collection's report descriptor.</summary>
public sealed record HidReportLayout(HidUsage? TopLevelUsage, IReadOnlyList<HidReportInfo> Reports)
{
    public static HidReportLayout FromRawDescriptor(byte[] rawDescriptor) => From(new ReportDescriptor(rawDescriptor));

    internal static HidReportLayout From(ReportDescriptor descriptor)
    {
        HidUsage? usage = null;
        foreach (var item in descriptor.DeviceItems)
        {
            foreach (uint value in item.Usages.GetAllValues())
            {
                usage = HidUsage.FromExtended(value);
                break;
            }

            if (usage is not null)
            {
                break;
            }
        }

        var reports = new List<HidReportInfo>();
        Add(reports, HidReportKind.Input, descriptor.InputReports);
        Add(reports, HidReportKind.Output, descriptor.OutputReports);
        Add(reports, HidReportKind.Feature, descriptor.FeatureReports);
        return new HidReportLayout(usage, reports);
    }

    private static void Add(List<HidReportInfo> into, HidReportKind kind, IEnumerable<Report> reports)
    {
        foreach (var report in reports.OrderBy(r => r.ReportID))
        {
            into.Add(new HidReportInfo(kind, report.ReportID, report.Length));
        }
    }
}
