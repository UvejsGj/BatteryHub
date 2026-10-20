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
    public async Task A_dual_mode_device_is_keyed_by_its_classic_address()
    {
        _source.Devices.Add(Device("buds", 0x5A5A5A5A5A5A) with { ClassicAddress = 0xA0AB51C0FFEE });
        _source.SetRead("buds", Plain55);
        using var provider = Provider();

        Assert.Equal("bt-a0ab51c0ffee", Assert.Single(await provider.PollAsync(Ct)).DeviceId);
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
    public void Device_changes_are_settled_into_one_poll_and_a_push()
    {
        _source.Devices.Add(Device("kb", 8));
        _source.SetRead("kb", Plain55);
        using var provider = Provider();
        var pushes = new List<IReadOnlyList<BatteryReading>>();
        provider.ReadingsChanged += (_, e) => pushes.Add(e.Readings);

        _source.RaiseDevicesChanged();
        _time.Advance(TimeSpan.FromSeconds(1.5));
        _source.RaiseDevicesChanged(); // Windows raises several updates while one device connects
        _time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Empty(_source.ReadLog); // not until the changes have been quiet for 2 s
        _time.Advance(TimeSpan.FromSeconds(1));
        _time.Advance(TimeSpan.FromMinutes(10)); // one poll per burst, never a repeating timer

        Assert.Single(_source.ReadLog);
        Assert.Equal(55, Assert.Single(Assert.Single(pushes)).Percent);
        Assert.Equal(TimeSpan.FromMinutes(5), provider.PollInterval);
    }

    [Fact]
    public void Nothing_is_read_after_a_change_when_nobody_listens()
    {
        _source.Devices.Add(Device("kb", 8));
        _source.SetRead("kb", Plain55);
        using var provider = Provider();

        _source.RaiseDevicesChanged();
        _time.Advance(TimeSpan.FromSeconds(3));

        Assert.Empty(_source.ReadLog);
    }

    [Fact]
    public async Task A_poll_after_a_change_waits_for_one_already_running()
    {
        // Two polls at once would both read the device and race on the skip state.
        var source = new BlockingSource();
        using var provider = new BleBatteryProvider(source, time: _time);
        var pushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.ReadingsChanged += (_, _) => pushed.TrySetResult();

        var running = provider.PollAsync(Ct); // held inside the read
        source.Raise();
        _time.Advance(TimeSpan.FromSeconds(3)); // the change poll starts now

        Assert.Equal(1, source.MaxInFlight);
        source.Release.SetResult();
        await running;
        await pushed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal((2, 1), (source.Reads, source.MaxInFlight));
    }

    [Fact]
    public void Dispose_releases_the_source_and_stops_pushing()
    {
        _source.Devices.Add(Device("kb", 8));
        _source.SetRead("kb", Plain55);
        var provider = Provider();
        int pushes = 0;
        provider.ReadingsChanged += (_, _) => pushes++;
        _source.RaiseDevicesChanged();

        provider.Dispose();
        _time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal((true, 0), (_source.Disposed, pushes));
        Assert.Empty(_source.ReadLog);
    }

    private sealed class BlockingSource : IBleBatterySource
    {
        private int _inFlight;

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Reads;

        public int MaxInFlight;

        public event EventHandler? DevicesChanged;

        public void Raise() => DevicesChanged?.Invoke(this, EventArgs.Empty);

        public Task<IReadOnlyList<BleDeviceInfo>> ListPairedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BleDeviceInfo>>([new("kb", "Keyboard", 8, true, null)]);

        public async Task<BleBatteryRead> ReadAsync(BleDeviceInfo device, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Reads);
            MaxInFlight = Math.Max(MaxInFlight, Interlocked.Increment(ref _inFlight));
            await Release.Task;
            Interlocked.Decrement(ref _inFlight);
            return new BleBatteryRead(BleReadStatus.Ok, 55);
        }

        public void Dispose()
        {
        }
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
