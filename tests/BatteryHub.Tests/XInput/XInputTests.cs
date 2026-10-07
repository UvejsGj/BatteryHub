using BatteryHub.Core;
using BatteryHub.Core.XInput;
using Microsoft.Extensions.Time.Testing;

namespace BatteryHub.Tests.XInput;

public class XInputTests
{
    [Theory]
    [InlineData(0x02, 0x00, CoarseLevel.Empty)]
    [InlineData(0x02, 0x01, CoarseLevel.Low)]
    [InlineData(0x03, 0x02, CoarseLevel.Medium)]
    [InlineData(0x03, 0x03, CoarseLevel.Full)]
    public void Battery_powered_pads_show_their_level(byte type, byte level, CoarseLevel expected)
    {
        Assert.Equal(expected, new XInputBattery(type, level).ShownLevel);
    }

    [Theory]
    [InlineData(0x00)] // disconnected
    [InlineData(0x01)] // wired, including Steam Input and DS4Windows virtual pads
    [InlineData(0xFF)] // unknown
    public void Pads_without_a_known_battery_are_not_shown(byte type)
    {
        for (int level = 0; level <= 0xFF; level++)
        {
            Assert.Null(new XInputBattery(type, (byte)level).ShownLevel);
        }
    }

    [Fact]
    public void Levels_outside_the_four_documented_ones_are_not_shown()
    {
        Assert.Null(new XInputBattery(0x02, 0x04).ShownLevel);
        Assert.Null(new XInputBattery(0x03, 0xFF).ShownLevel);
    }

    private sealed class FakeSource(params XInputBattery?[] slots) : IXInputSource
    {
        public Exception? Error { get; init; }

        public Dictionary<int, XInputHardware> Hardware { get; } = [];

        public XInputBattery? GetGamepadBattery(int userIndex) =>
            Error is not null ? throw Error : userIndex < slots.Length ? slots[userIndex] : null;

        public XInputHardware? GetHardware(int userIndex) => Hardware.TryGetValue(userIndex, out var hardware) ? hardware : null;
    }

    [Fact]
    public async Task Reports_battery_pads_by_slot_and_skips_the_rest()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        var source = new FakeSource(new XInputBattery(0x01, 0x03), null, new XInputBattery(0x03, 0x01), new XInputBattery(0x02, 0x03));
        using var provider = new XInputProvider(source, time: time);

        var readings = await provider.PollAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["xinput-3", "xinput-4"], readings.Select(r => r.DeviceId));
        var low = readings[0];
        Assert.Equal(("Xbox controller 3", DeviceKind.Gamepad, ConnectionType.Wireless, CoarseLevel.Low, (int?)null, ReadingStatus.Ok, "xinput"),
            (low.Name, low.Kind, low.Connection, low.CoarseLevel, low.Percent, low.Status, low.Source));
        Assert.Equal(time.GetUtcNow(), low.Timestamp);
    }

    [Fact]
    public async Task A_pad_that_has_not_reported_yet_shows_no_data_not_empty()
    {
        using var provider = new XInputProvider(new FakeSource(new XInputBattery(0x00, 0x00)));

        var reading = Assert.Single(await provider.PollAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ReadingStatus.NoData, reading.Status);
        Assert.Null(reading.CoarseLevel);
        Assert.Contains("Waiting", reading.StatusDetail);
    }

    [Theory]
    [InlineData(0x045E, 0x0B13)] // Xbox Series pad on Bluetooth LE
    [InlineData(0x045E, 0x02FD)] // Xbox One S pad on Bluetooth
    [InlineData(0x28DE, 0x11FF)] // Steam Input virtual gamepad
    public async Task Pads_shown_better_by_another_reader_are_skipped(int vendor, int product)
    {
        var source = new FakeSource(new XInputBattery(0x02, 0x02), new XInputBattery(0x02, 0x03));
        source.Hardware[0] = new XInputHardware((ushort)vendor, (ushort)product);
        using var provider = new XInputProvider(source);

        var reading = Assert.Single(await provider.PollAsync(TestContext.Current.CancellationToken));

        Assert.Equal("xinput-2", reading.DeviceId);
    }

    [Theory]
    [InlineData(0x045E, 0x02FF)] // Xbox pad through the wireless adapter (XBOXGIP driver)
    [InlineData(0x045E, 0x0B12)] // Xbox Series pad on USB or the adapter
    [InlineData(0x28DE, 0x1102)] // another Valve product
    public void Other_hardware_is_kept(int vendor, int product)
    {
        Assert.False(new XInputHardware((ushort)vendor, (ushort)product).IsCoveredElsewhere);
    }

    [Fact]
    public async Task Missing_xinput_gives_no_readings_instead_of_failing()
    {
        using var provider = new XInputProvider(new FakeSource { Error = new DllNotFoundException("xinput1_4.dll") });

        Assert.Empty(await provider.PollAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await provider.PollAsync(TestContext.Current.CancellationToken));
    }
}
