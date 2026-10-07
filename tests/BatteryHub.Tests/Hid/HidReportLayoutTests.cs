using BatteryHub.Core.Hid;

namespace BatteryHub.Tests.Hid;

public class HidReportLayoutTests
{
    // Hand-written descriptor: a game pad with a 63-byte input report 0x01
    // and a 40-byte vendor feature report 0x05.
    private static readonly byte[] GamePadWithReportIds =
    [
        0x05, 0x01,       // Usage Page (Generic Desktop)
        0x09, 0x05,       // Usage (Game Pad)
        0xA1, 0x01,       // Collection (Application)
        0x85, 0x01,       //   Report ID (0x01)
        0x09, 0x30,       //   Usage (X)
        0x15, 0x00,       //   Logical Minimum (0)
        0x26, 0xFF, 0x00, //   Logical Maximum (255)
        0x75, 0x08,       //   Report Size (8)
        0x95, 0x3F,       //   Report Count (63)
        0x81, 0x02,       //   Input (Data, Var, Abs)
        0x85, 0x05,       //   Report ID (0x05)
        0x06, 0x00, 0xFF, //   Usage Page (Vendor 0xFF00)
        0x09, 0x22,       //   Usage (0x22)
        0x95, 0x28,       //   Report Count (40)
        0xB1, 0x02,       //   Feature (Data, Var, Abs)
        0xC0,             // End Collection
    ];

    // A mouse-style descriptor with no Report ID items: one 3-byte input report.
    private static readonly byte[] NoReportIds =
    [
        0x05, 0x01, // Usage Page (Generic Desktop)
        0x09, 0x02, // Usage (Mouse)
        0xA1, 0x01, // Collection (Application)
        0x09, 0x30, //   Usage (X)
        0x75, 0x08, //   Report Size (8)
        0x95, 0x03, //   Report Count (3)
        0x81, 0x06, //   Input (Data, Var, Rel)
        0xC0,       // End Collection
    ];

    [Fact]
    public void Reads_top_level_usage()
    {
        var layout = HidReportLayout.FromRawDescriptor(GamePadWithReportIds);

        Assert.Equal(new HidUsage(0x0001, 0x0005), layout.TopLevelUsage);
    }

    [Fact]
    public void Report_lengths_count_the_report_id_byte()
    {
        // Our byte offsets put the report ID at index 0, so lengths must include it.
        var layout = HidReportLayout.FromRawDescriptor(GamePadWithReportIds);

        Assert.Equal(
            [
                new HidReportInfo(HidReportKind.Input, 0x01, 64),
                new HidReportInfo(HidReportKind.Feature, 0x05, 41),
            ],
            layout.Reports);
    }

    [Fact]
    public void Report_without_id_is_listed_as_id_zero()
    {
        var layout = HidReportLayout.FromRawDescriptor(NoReportIds);

        Assert.Equal([new HidReportInfo(HidReportKind.Input, 0x00, 4)], layout.Reports);
    }

    [Fact]
    public void Formats_usage_as_page_and_id()
    {
        Assert.Equal("0x0001:0x0005", HidUsage.FromExtended(0x0001_0005).ToString());
    }
}
