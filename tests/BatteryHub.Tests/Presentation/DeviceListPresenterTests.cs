using BatteryHub.Core;
using BatteryHub.Core.Presentation;

namespace BatteryHub.Tests.Presentation;

public class DeviceListPresenterTests
{
    private static BatteryReading Reading(
        int? percent = null,
        ChargeState charge = ChargeState.Discharging,
        ReadingStatus status = ReadingStatus.Ok,
        CoarseLevel? coarse = null,
        string? detail = null,
        ConnectionType connection = ConnectionType.Bluetooth,
        string name = "DualShock 4") => new()
    {
        DeviceId = "sony-a0ab51c0ffee",
        Name = name,
        Kind = DeviceKind.Gamepad,
        Connection = connection,
        Percent = percent,
        CoarseLevel = coarse,
        ChargeState = charge,
        Status = status,
        StatusDetail = detail,
        Timestamp = DateTimeOffset.UnixEpoch,
        Source = "test",
    };

    [Fact]
    public void Percentage_row()
    {
        var row = DeviceListPresenter.Row(Reading(85, ChargeState.Charging));

        Assert.Equal(("DualShock 4", "85%", "Bluetooth · Charging", null, 0.85, false), (row.Name, row.Level, row.Details, row.Problem, row.BarFraction, row.IsLow));
    }

    [Fact]
    public void Coarse_level_shows_its_name_never_a_percentage()
    {
        var row = DeviceListPresenter.Row(Reading(coarse: CoarseLevel.Medium, connection: ConnectionType.Usb));

        Assert.Equal("Medium", row.Level);
        Assert.DoesNotContain("%", row.Level);
        Assert.Equal("USB", row.Details);
    }

    [Theory]
    [InlineData(20, ChargeState.Discharging, true)]
    [InlineData(21, ChargeState.Discharging, false)]
    [InlineData(5, ChargeState.Charging, false)]
    public void Low_means_at_or_below_the_threshold_and_not_charging(int percent, ChargeState charge, bool low)
    {
        Assert.Equal(low, DeviceListPresenter.Row(Reading(percent, charge)).IsLow);
    }

    [Theory]
    [InlineData(CoarseLevel.Empty, "Empty", true)]
    [InlineData(CoarseLevel.Low, "Low", true)]
    [InlineData(CoarseLevel.Medium, "Medium", false)]
    [InlineData(CoarseLevel.Full, "Full", false)]
    public void Coarse_levels_show_their_own_name_and_low_ones_are_low(CoarseLevel level, string text, bool lowWhenDischarging)
    {
        var discharging = DeviceListPresenter.Row(Reading(coarse: level));
        var charging = DeviceListPresenter.Row(Reading(coarse: level, charge: ChargeState.Charging));

        Assert.Equal(text, discharging.Level);
        Assert.NotNull(discharging.BarFraction);
        Assert.Equal(lowWhenDischarging, discharging.IsLow);
        Assert.False(charging.IsLow);
    }

    [Theory]
    [InlineData(ReadingStatus.InUseByAnotherApp, "In use", "In use by another app")]
    [InlineData(ReadingStatus.DeviceError, "Fault", "Charging error")]
    [InlineData(ReadingStatus.NoData, "No data", "The pad sent no reports")]
    public void Problems_show_a_short_level_and_the_reason(ReadingStatus status, string level, string detail)
    {
        var row = DeviceListPresenter.Row(Reading(status: status, detail: detail, charge: ChargeState.Unknown));

        Assert.Equal(level, row.Level);
        Assert.Equal(detail, row.Problem);
        Assert.Null(row.BarFraction);
        Assert.Equal("Bluetooth", row.Details);
    }

    [Fact]
    public void Tooltip_lists_devices_and_charge_state()
    {
        string tip = DeviceListPresenter.ToolTip([Reading(85, ChargeState.Charging), Reading(40, name: "AirPods Pro 3")]);

        Assert.Equal("DualShock 4: 85% (charging)\nAirPods Pro 3: 40%", tip);
    }

    [Fact]
    public void Tooltip_without_devices_says_so()
    {
        Assert.Equal("BatteryHub: no devices", DeviceListPresenter.ToolTip([]));
    }

    [Fact]
    public void Tooltip_is_cut_to_what_windows_shows()
    {
        var many = Enumerable.Range(0, 20).Select(i => Reading(50, name: $"Controller number {i}"));

        string tip = DeviceListPresenter.ToolTip(many);

        Assert.Equal(DeviceListPresenter.MaxToolTipLength, tip.Length);
        Assert.EndsWith("…", tip);
    }
}
