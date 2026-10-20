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

        public XInputBattery?[] Slots { get; set; } = slots;

        public Dictionary<int, XInputHardware> Hardware { get; } = [];

        public XInputBattery? GetGamepadBattery(int userIndex) =>
            Error is not null ? throw Error : userIndex < Slots.Length ? Slots[userIndex] : null;

        public XInputHardware? GetHardware(int userIndex) => Hardware.TryGetValue(userIndex, out var hardware) ? hardware : null;
    }

    [Fact]
    public async Task Reports_battery_pads_by_slot_and_skips_the_rest()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        // Wired, unknown battery type, NiMH low, alkaline full.
        var source = new FakeSource(new XInputBattery(0x01, 0x03), new XInputBattery(0xFF, 0x03), new XInputBattery(0x03, 0x01), new XInputBattery(0x02, 0x03));
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
    [InlineData(0x045E, 0x0B20)] // Xbox One S pad on Bluetooth LE
    [InlineData(0x045E, 0x0B21)] // Adaptive controller on Bluetooth LE
    [InlineData(0x045E, 0x0B22)] // Elite 2 on Bluetooth LE
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
    [InlineData(0x00)] // not reported yet
    [InlineData(0x01)] // wired
    [InlineData(0xFF)] // unknown
    public async Task Bluetooth_rules_apply_whatever_battery_type_xinput_reports(int type)
    {
        // XInput's battery data for pads on Bluetooth is unreliable, so the type cannot be relied on either.
        var source = new FakeSource(new XInputBattery((byte)type, 0x00), new XInputBattery((byte)type, 0x00));
        source.Hardware[0] = new XInputHardware(0x045E, 0x0B13); // Bluetooth LE: left to the Bluetooth readers
        source.Hardware[1] = new XInputHardware(0x045E, 0x02FD); // classic Bluetooth: "no data"
        using var provider = new XInputProvider(source);

        var reading = Assert.Single(await provider.PollAsync(TestContext.Current.CancellationToken));

        Assert.Equal(("xinput-2", ConnectionType.Bluetooth), (reading.DeviceId, reading.Connection));
        Assert.Contains("classic Bluetooth", reading.StatusDetail);
    }

    [Theory]
    [InlineData(0x02E0)] // Xbox One S, first revision
    [InlineData(0x02FD)] // Xbox One S
    [InlineData(0x0B05)] // Elite 2
    [InlineData(0x0B0C)] // Adaptive controller
    public async Task Pads_on_classic_bluetooth_show_no_data_instead_of_an_unreliable_level(int product)
    {
        var source = new FakeSource(new XInputBattery(0x02, 0x00));
        source.Hardware[0] = new XInputHardware(0x045E, (ushort)product);
        using var provider = new XInputProvider(source);

        var reading = Assert.Single(await provider.PollAsync(TestContext.Current.CancellationToken));

        Assert.Equal(("xinput-1", ReadingStatus.NoData, (CoarseLevel?)null, ConnectionType.Bluetooth),
            (reading.DeviceId, reading.Status, reading.CoarseLevel, reading.Connection));
        Assert.Contains("classic Bluetooth", reading.StatusDetail);
    }

    [Theory]
    [InlineData(0x045E, 0x02FF)] // Xbox pad through the wireless adapter (XBOXGIP driver)
    [InlineData(0x045E, 0x0B12)] // Xbox Series pad on USB or the adapter
    [InlineData(0x045E, 0x0000)] // wireless Xbox 360 pad (reports product 0)
    [InlineData(0x0E6F, 0x02FD)] // another vendor with a product ID that matches a Microsoft one
    [InlineData(0x0E6F, 0x0B13)]
    [InlineData(0x0E6F, 0x11FF)] // ... or Steam's virtual pad
    [InlineData(0x28DE, 0x1102)] // another Valve product
    public void Other_hardware_is_kept(int vendor, int product)
    {
        var hardware = new XInputHardware((ushort)vendor, (ushort)product);
        Assert.False(hardware.IsCoveredElsewhere);
        Assert.False(hardware.IsClassicBluetooth);
    }

    [Fact]
    public async Task A_first_empty_waits_until_it_holds()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        var source = new FakeSource(new XInputBattery(0x02, 0x00));
        using var provider = new XInputProvider(source, time: time);
        var ct = TestContext.Current.CancellationToken;

        var held = Assert.Single(await provider.PollAsync(ct));
        Assert.Equal((ReadingStatus.NoData, XInputProvider.EmptyHeldDetail), (held.Status, held.StatusDetail));
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(ReadingStatus.NoData, Assert.Single(await provider.PollAsync(ct)).Status);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(CoarseLevel.Empty, Assert.Single(await provider.PollAsync(ct)).CoarseLevel);

        // Once shown, it stays shown: a dying pad must not flip between "Empty" and "no data".
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(CoarseLevel.Empty, Assert.Single(await provider.PollAsync(ct)).CoarseLevel);
    }

    [Theory]
    [InlineData(0x02)] // medium
    [InlineData(0x03)] // full
    public async Task A_jump_to_empty_from_a_high_level_waits_like_a_fresh_connection(byte previous)
    {
        // Polls are 30 s apart: a pad can drop and reconnect unseen, then report its connect-time EMPTY.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        var source = new FakeSource(new XInputBattery(0x03, previous));
        using var provider = new XInputProvider(source, time: time);
        var ct = TestContext.Current.CancellationToken;
        await provider.PollAsync(ct);

        source.Slots = [new XInputBattery(0x03, 0x00)];
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(ReadingStatus.NoData, Assert.Single(await provider.PollAsync(ct)).Status);
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(CoarseLevel.Empty, Assert.Single(await provider.PollAsync(ct)).CoarseLevel);
    }

    [Fact]
    public async Task The_empty_hold_is_per_slot()
    {
        var source = new FakeSource(new XInputBattery(0x02, 0x01), null, null);
        using var provider = new XInputProvider(source);
        var ct = TestContext.Current.CancellationToken;
        await provider.PollAsync(ct); // slot 1 reported LOW

        source.Slots = [new XInputBattery(0x02, 0x00), null, new XInputBattery(0x02, 0x00)];
        var readings = await provider.PollAsync(ct);

        Assert.Equal(CoarseLevel.Empty, readings.Single(r => r.DeviceId == "xinput-1").CoarseLevel);
        Assert.Equal(ReadingStatus.NoData, readings.Single(r => r.DeviceId == "xinput-3").Status);
    }

    [Fact]
    public async Task Empty_after_a_real_level_shows_at_once()
    {
        var source = new FakeSource(new XInputBattery(0x02, 0x01));
        using var provider = new XInputProvider(source);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(CoarseLevel.Low, Assert.Single(await provider.PollAsync(ct)).CoarseLevel);

        source.Slots = [new XInputBattery(0x02, 0x00)];

        Assert.Equal(CoarseLevel.Empty, Assert.Single(await provider.PollAsync(ct)).CoarseLevel);
    }

    [Theory]
    [InlineData(false, 0x01)] // the pad left the slot
    [InlineData(true, 0x01)] // the slot reported "no data yet" again, e.g. after a reconnect
    [InlineData(false, 0x00)] // ... and the old pad's own held EMPTY does not count for the new one
    [InlineData(true, 0x00)]
    public async Task A_new_pad_in_the_slot_starts_afresh(bool awaitingData, byte firstLevel)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        var source = new FakeSource(new XInputBattery(0x02, firstLevel));
        using var provider = new XInputProvider(source, time: time);
        var ct = TestContext.Current.CancellationToken;
        await provider.PollAsync(ct);

        source.Slots = [awaitingData ? new XInputBattery(0x00, 0x00) : null];
        await provider.PollAsync(ct);
        source.Slots = [new XInputBattery(0x02, 0x00)];
        time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(ReadingStatus.NoData, Assert.Single(await provider.PollAsync(ct)).Status);
    }

    [Theory]
    [InlineData(0x0B13)] // a Bluetooth LE pad in between
    [InlineData(0x02FD)] // a classic Bluetooth pad in between
    public async Task A_bluetooth_pad_in_between_does_not_lend_its_slot_state(int product)
    {
        var source = new FakeSource(new XInputBattery(0x02, 0x01));
        using var provider = new XInputProvider(source);
        var ct = TestContext.Current.CancellationToken;
        await provider.PollAsync(ct); // an adapter pad at LOW: its EMPTY would show at once

        source.Hardware[0] = new XInputHardware(0x045E, (ushort)product);
        await provider.PollAsync(ct);
        source.Hardware.Clear();
        source.Slots = [new XInputBattery(0x02, 0x00)]; // a new adapter pad's connect-time EMPTY

        Assert.Equal(ReadingStatus.NoData, Assert.Single(await provider.PollAsync(ct)).Status);
    }

    public static TheoryData<Exception> Unavailable => [new DllNotFoundException("xinput1_4.dll"), new EntryPointNotFoundException("XInputGetBatteryInformation")];

    [Theory]
    [MemberData(nameof(Unavailable))]
    public async Task Missing_xinput_gives_no_readings_instead_of_failing(Exception error)
    {
        using var provider = new XInputProvider(new FakeSource { Error = error });

        Assert.Empty(await provider.PollAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await provider.PollAsync(TestContext.Current.CancellationToken));
    }
}
