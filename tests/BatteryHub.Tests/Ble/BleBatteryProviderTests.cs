using BatteryHub.Core;
using BatteryHub.Core.Ble;
using Microsoft.Extensions.Time.Testing;

namespace BatteryHub.Tests.Ble;

public class BleBatteryProviderTests
{
    private const ushort KeyboardAppearance = (0x00F << 6) | 0x01;
    private static readonly BleBatteryRead Plain55 = new(BleReadStatus.Ok, 55);

    private readonly FakeBleSource _source = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));

    private BleBatteryProvider Provider() => new(_source, time: _time);

    private BleDeviceInfo Device(string id, ulong address, bool connected = true, ushort? appearance = null) => new(id, $"Device {id}", address, connected, appearance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_connected_devices_into_bluetooth_keyed_readings()
    {
        _source.Devices.Add(Device("kb", 0xA0AB51C0FFEE, appearance: KeyboardAppearance));
        _source.SetRead("kb", Plain55);
        using var provider = Provider();

        var reading = Assert.Single(await provider.PollAsync(Ct));

        Assert.Equal(("bt-a0ab51c0ffee", "Device kb", DeviceKind.Keyboard, ConnectionType.Ble, (int?)55, "ble-gatt"),
            (reading.DeviceId, reading.Name, reading.Kind, reading.Connection, reading.Percent, reading.Source));
    }

    [Fact]
    public async Task Never_reads_a_device_that_is_not_connected()
    {
        _source.Devices.Add(Device("off", 1, connected: false));
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
        Assert.Empty(_source.ReadLog);
    }

    [Theory]
    [InlineData(BleReadStatus.AccessDenied)]
    [InlineData(BleReadStatus.NoBatteryService)]
    public async Task Refused_or_batteryless_devices_are_skipped_quietly_until_they_reconnect(BleReadStatus status)
    {
        var device = Device("mouse", 2);
        _source.Devices.Add(device);
        _source.SetRead("mouse", new BleBatteryRead(status, null), Plain55);
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
        Assert.Empty(await provider.PollAsync(Ct));
        Assert.Single(_source.ReadLog); // not retried while it stays connected

        _source.Devices[0] = device with { IsConnected = false };
        await provider.PollAsync(Ct);
        _source.Devices[0] = device;
        Assert.Equal(55, Assert.Single(await provider.PollAsync(Ct)).Percent);
    }

    [Fact]
    public async Task Invalid_levels_are_not_shown()
    {
        _source.Devices.Add(Device("odd", 3));
        _source.SetRead("odd", new BleBatteryRead(BleReadStatus.Ok, 101));
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
    }

    [Theory]
    [InlineData(BleReadStatus.Unreachable)]
    [InlineData(BleReadStatus.ProtocolError)]
    public async Task Transient_failures_are_retried_next_poll(BleReadStatus status)
    {
        _source.Devices.Add(Device("flaky", 4));
        _source.SetRead("flaky", new BleBatteryRead(status, null), Plain55);
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
        Assert.Equal(55, Assert.Single(await provider.PollAsync(Ct)).Percent);
    }

    [Fact]
    public async Task Connected_devices_are_read_on_every_poll()
    {
        _source.Devices.Add(Device("kb", 6));
        _source.SetRead("kb", new BleBatteryRead(BleReadStatus.Ok, 80), Plain55);
        using var provider = Provider();

        Assert.Equal(80, Assert.Single(await provider.PollAsync(Ct)).Percent);
        Assert.Equal(55, Assert.Single(await provider.PollAsync(Ct)).Percent);
    }

    [Fact]
    public async Task A_device_that_disconnects_drops_out()
    {
        var device = Device("kb", 7);
        _source.Devices.Add(device);
        _source.SetRead("kb", Plain55);
        using var provider = Provider();
        Assert.Single(await provider.PollAsync(Ct));

        _source.Devices[0] = device with { IsConnected = false };

        Assert.Empty(await provider.PollAsync(Ct));
    }

    [Fact]
    public async Task A_read_that_throws_counts_as_unreachable_and_does_not_fail_the_poll()
    {
        _source.Devices.Add(Device("a", 9));
        _source.Devices.Add(Device("b", 10, connected: false));
        _source.ReadError = new System.Runtime.InteropServices.COMException("device gone");
        using var provider = Provider();

        var inspections = await provider.InspectAsync(Ct);

        Assert.Equal(BleReadStatus.Unreachable, inspections[0].Read?.Status);
        Assert.Null(inspections[0].Reading);
    }

    [Fact]
    public async Task The_source_timing_out_does_not_fail_the_poll()
    {
        _source.Devices.Add(Device("slow", 11));
        _source.ReadError = new OperationCanceledException("read timed out");
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
    }

    [Fact]
    public async Task A_device_change_triggers_a_poll_and_a_push()
    {
        _source.Devices.Add(Device("kb", 8));
        _source.SetRead("kb", Plain55);
        using var provider = Provider();
        var pushed = new TaskCompletionSource<IReadOnlyList<BatteryReading>>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.ReadingsChanged += (_, e) => pushed.TrySetResult(e.Readings);

        _source.RaiseDevicesChanged();
        _time.Advance(TimeSpan.FromSeconds(3));

        var readings = await pushed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(55, Assert.Single(readings).Percent);
    }

    [Fact]
    public void Dispose_releases_the_source()
    {
        var provider = Provider();

        provider.Dispose();

        Assert.True(_source.Disposed);
    }

    [Theory]
    [InlineData(null, DeviceKind.Other)]
    [InlineData((0x00F << 6) | 0x01, DeviceKind.Keyboard)]
    [InlineData((0x00F << 6) | 0x02, DeviceKind.Mouse)]
    [InlineData((0x00F << 6) | 0x04, DeviceKind.Gamepad)]
    [InlineData((0x00F << 6) | 0x03, DeviceKind.Gamepad)]
    [InlineData(0x00F << 6, DeviceKind.Other)]
    [InlineData(0x0040, DeviceKind.Other)] // phone
    public void Kind_comes_from_the_appearance(int? appearance, DeviceKind kind)
    {
        Assert.Equal(kind, BleBattery.KindFromAppearance((ushort?)appearance));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(101, null)]
    [InlineData(255, null)]
    public void Battery_level_must_be_a_percentage(int value, int? percent)
    {
        Assert.Equal(percent, BleBattery.Percent((byte)value));
    }
}
