using BatteryHub.Core;
using BatteryHub.Core.BluetoothProperty;
using Microsoft.Extensions.Time.Testing;

namespace BatteryHub.Tests.BluetoothProperty;

public class BluetoothPropertyProviderTests
{
    private const ulong HeadsetAddress = 0xA0AB51C0FFEE;
    private const ulong MouseAddress = 0xC0FFEE123456;
    private const ushort MouseAppearance = (0x00F << 6) | 0x02;

    private readonly FakeBluetoothPropertySource _source = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private BluetoothPropertyProvider Provider() => new(_source, time: _time);

    private static BluetoothBatteryNode HandsFreeNode(int? percent, ulong address = HeadsetAddress, Guid? container = null) =>
        new($@"BTHENUM\{{0000111E-0000-1000-8000-00805F9B34FB}}_X\8&1&0&{address:X12}_C00000000", BluetoothNodeKind.HandsFree,
            address, "WH-1000XM4 Hands-Free AG", container, percent, percent is null ? "not set" : $"BYTE ({percent})");

    private static BluetoothBatteryNode LowEnergyNode(int? percent, ulong address = MouseAddress, Guid? container = null) =>
        new($@"BTHLE\DEV_{address:X12}\7&1&0&{address:x12}", BluetoothNodeKind.LowEnergy,
            address, "MX Anywhere 3", container, percent, percent is null ? "not set" : $"BYTE ({percent})");

    private static BluetoothEndpoint Classic(bool connected, ulong address = HeadsetAddress, string name = "WH-1000XM4", Guid? container = null) =>
        new(address, false, name, connected, container, null);

    private static BluetoothEndpoint Le(bool connected, ulong address = MouseAddress, Guid? container = null) =>
        new(address, true, "MX Anywhere 3", connected, container, MouseAppearance);

    [Fact]
    public async Task Shows_a_connected_headsets_stored_level()
    {
        _source.Nodes.Add(HandsFreeNode(70));
        _source.Endpoints.Add(Classic(connected: true));
        using var provider = Provider();

        var reading = Assert.Single(await provider.PollAsync(Ct));

        Assert.Equal(("bt-a0ab51c0ffee", "WH-1000XM4", DeviceKind.Headset, ConnectionType.Bluetooth, (int?)70, ReadingStatus.Ok, "bt-property"),
            (reading.DeviceId, reading.Name, reading.Kind, reading.Connection, reading.Percent, reading.Status, reading.Source));
        Assert.Equal(ChargeState.Unknown, reading.ChargeState);
        Assert.Null(reading.CoarseLevel);
        Assert.Equal(_time.GetUtcNow(), reading.Timestamp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Never_shows_the_level_Windows_kept_after_a_disconnect(bool lowEnergy)
    {
        _source.Nodes.Add(lowEnergy ? LowEnergyNode(1) : HandsFreeNode(1));
        _source.Endpoints.Add(lowEnergy ? Le(connected: false) : Classic(connected: false));
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
        var inspection = Assert.Single(await provider.InspectAsync(Ct));
        Assert.Contains("not connected", inspection.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ignores_nodes_without_a_paired_device()
    {
        _source.Nodes.Add(HandsFreeNode(70));
        _source.Endpoints.Add(Classic(connected: true, address: 0x111111111111));
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
    }

    [Fact]
    public async Task A_connected_headset_without_a_level_shows_no_data_never_zero()
    {
        _source.Nodes.Add(HandsFreeNode(null));
        _source.Endpoints.Add(Classic(connected: true, name: "AirPods Pro"));
        using var provider = Provider();

        var reading = Assert.Single(await provider.PollAsync(Ct));

        Assert.Equal((ReadingStatus.NoData, (int?)null, "AirPods Pro"), (reading.Status, reading.Percent, reading.Name));
        Assert.False(string.IsNullOrEmpty(reading.StatusDetail));
    }

    [Fact]
    public async Task Shows_a_connected_le_devices_level_with_its_appearance()
    {
        _source.Nodes.Add(LowEnergyNode(55));
        _source.Endpoints.Add(Le(connected: true));
        using var provider = Provider();

        var reading = Assert.Single(await provider.PollAsync(Ct));

        Assert.Equal(("bt-c0ffee123456", DeviceKind.Mouse, ConnectionType.Ble, (int?)55),
            (reading.DeviceId, reading.Kind, reading.Connection, reading.Percent));
    }

    [Fact]
    public async Task Leaves_le_devices_without_a_level_to_the_gatt_reader()
    {
        _source.Nodes.Add(LowEnergyNode(null));
        _source.Endpoints.Add(Le(connected: true));
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
    }

    [Fact]
    public async Task Matches_only_endpoints_of_the_same_transport()
    {
        // A dual-mode device: the classic link is up, the LE one is not.
        _source.Nodes.Add(LowEnergyNode(40, address: HeadsetAddress));
        _source.Endpoints.Add(Classic(connected: true));
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
    }

    [Fact]
    public async Task Never_uses_another_nodes_connection_state()
    {
        // E.g. a second earbud whose node has no endpoint of its own: its stored level may be months old.
        var container = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        _source.Nodes.Add(LowEnergyNode(64, address: 0x5A5A5A5A5A5A, container: container));
        _source.Endpoints.Add(Le(connected: true, address: 0x6B6B6B6B6B6B, container: container));
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
    }

    [Fact]
    public async Task An_le_node_of_a_dual_mode_device_is_keyed_by_its_classic_address()
    {
        var container = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        _source.Nodes.Add(HandsFreeNode(null, container: container));
        _source.Nodes.Add(LowEnergyNode(64, container: container));
        _source.Endpoints.Add(Classic(connected: true, container: container));
        _source.Endpoints.Add(Le(connected: true, container: container));
        using var provider = Provider();

        // The hands-free node's "no data" and the LE node's level are one device: one row, with the level.
        var reading = Assert.Single(await provider.PollAsync(Ct));

        Assert.Equal(("bt-a0ab51c0ffee", (int?)64), (reading.DeviceId, reading.Percent));
    }

    [Theory]
    [InlineData(true, false)] // only the paired LE device carries the container
    [InlineData(false, true)] // only the LE node does
    public async Task The_container_join_works_from_either_side(bool onEndpoint, bool onNode)
    {
        var container = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        _source.Nodes.Add(LowEnergyNode(64, container: onNode ? container : null));
        _source.Endpoints.Add(Classic(connected: true, container: container));
        _source.Endpoints.Add(Le(connected: true, container: onEndpoint ? container : null));
        using var provider = Provider();

        Assert.Equal("bt-a0ab51c0ffee", Assert.Single(await provider.PollAsync(Ct)).DeviceId);
    }

    [Fact]
    public async Task An_le_only_device_keeps_its_own_address()
    {
        _source.Nodes.Add(LowEnergyNode(64, container: Guid.NewGuid()));
        _source.Endpoints.Add(Classic(connected: true, container: Guid.NewGuid()));
        _source.Endpoints.Add(Le(connected: true));
        using var provider = Provider();

        Assert.Equal("bt-c0ffee123456", Assert.Single(await provider.PollAsync(Ct)).DeviceId);
    }

    [Fact]
    public async Task An_address_match_wins_over_a_container_match()
    {
        var container = Guid.NewGuid();
        _source.Nodes.Add(HandsFreeNode(70, container: container));
        _source.Endpoints.Add(Classic(connected: false, address: 0x222222222222, container: container));
        _source.Endpoints.Add(Classic(connected: true));
        using var provider = Provider();

        Assert.Single(await provider.PollAsync(Ct));
    }

    [Fact]
    public async Task One_reading_per_device_preferring_a_level()
    {
        _source.Nodes.Add(HandsFreeNode(null));
        _source.Nodes.Add(HandsFreeNode(80));
        _source.Endpoints.Add(Classic(connected: true));
        using var provider = Provider();

        var reading = Assert.Single(await provider.PollAsync(Ct));

        Assert.Equal(80, reading.Percent);
    }

    [Theory]
    [InlineData("", "WH-1000XM4")]
    [InlineData("  ", "WH-1000XM4")]
    [InlineData("Sony headphones", "Sony headphones")]
    public async Task Names_the_device_after_its_paired_name_then_its_node(string pairedName, string expected)
    {
        _source.Nodes.Add(HandsFreeNode(70));
        _source.Endpoints.Add(Classic(connected: true, name: pairedName));
        using var provider = Provider();

        Assert.Equal(expected, Assert.Single(await provider.PollAsync(Ct)).Name);
    }

    [Fact]
    public async Task Falls_back_to_the_address_when_nothing_names_the_device()
    {
        _source.Nodes.Add(HandsFreeNode(70) with { FriendlyName = null });
        _source.Endpoints.Add(Classic(connected: true, name: ""));
        using var provider = Provider();

        Assert.Equal("bt-a0ab51c0ffee", Assert.Single(await provider.PollAsync(Ct)).Name);
    }

    [Fact]
    public async Task Skips_the_paired_device_query_when_there_are_no_nodes()
    {
        using var provider = Provider();

        Assert.Empty(await provider.PollAsync(Ct));
        Assert.Equal(0, _source.PairedQueries);
    }

    [Fact]
    public async Task A_failed_paired_device_query_fails_the_poll()
    {
        // The monitor logs it and keeps the previous readings.
        _source.Nodes.Add(HandsFreeNode(70));
        _source.PairedError = new TimeoutException();
        using var provider = Provider();

        await Assert.ThrowsAsync<TimeoutException>(() => provider.PollAsync(Ct));
    }

    [Fact]
    public async Task Polling_after_dispose_throws()
    {
        var provider = Provider();
        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.PollAsync(Ct));
    }

    [Fact]
    public void Polls_every_minute()
    {
        using var provider = Provider();

        Assert.Equal(TimeSpan.FromSeconds(60), provider.PollInterval);
        Assert.Equal("bt-property", provider.Name);
    }

    [Fact]
    public void A_disconnect_is_pushed_once_the_changes_settle()
    {
        _source.Nodes.Add(LowEnergyNode(55));
        _source.Endpoints.Add(Le(connected: false));
        using var provider = Provider();
        var pushes = new List<IReadOnlyList<BatteryReading>>();
        provider.ReadingsChanged += (_, e) => pushes.Add(e.Readings);

        _source.RaiseDevicesChanged();
        _time.Advance(TimeSpan.FromSeconds(1.5));
        _source.RaiseDevicesChanged(); // Windows raises several updates for one change
        _time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Equal(0, _source.PairedQueries); // not until the changes have been quiet for 2 s
        _time.Advance(TimeSpan.FromSeconds(1));
        _time.Advance(TimeSpan.FromMinutes(1)); // one poll per burst, never a repeating timer

        Assert.Empty(Assert.Single(pushes)); // the device's row goes within seconds, not at the next 60 s poll
        Assert.Equal(1, _source.PairedQueries);
    }

    [Fact]
    public void Nothing_is_read_after_a_change_when_nobody_listens()
    {
        _source.Nodes.Add(LowEnergyNode(55));
        using var provider = Provider();

        _source.RaiseDevicesChanged();
        _time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(0, _source.PairedQueries);
    }

    [Fact]
    public async Task A_poll_after_a_change_waits_for_one_already_running()
    {
        // Otherwise a poll that started before a disconnect could finish after the one that saw it.
        _source.Nodes.Add(LowEnergyNode(55));
        _source.Endpoints.Add(Le(connected: true));
        _source.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var provider = Provider();
        var pushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.ReadingsChanged += (_, _) => pushed.TrySetResult();

        var running = provider.PollAsync(Ct); // held inside the paired-device query
        _source.RaiseDevicesChanged();
        _time.Advance(TimeSpan.FromSeconds(3)); // the change poll starts now

        Assert.Equal((1, 1), (_source.PairedQueries, _source.MaxInFlight));
        _source.Hold.SetResult();
        await running;
        await pushed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal((2, 1), (_source.PairedQueries, _source.MaxInFlight));
    }

    [Fact]
    public void Dispose_stops_listening_and_disposes_the_source()
    {
        _source.Nodes.Add(LowEnergyNode(55));
        var provider = Provider();
        int pushes = 0;
        provider.ReadingsChanged += (_, _) => pushes++;
        _source.RaiseDevicesChanged();

        provider.Dispose();
        _time.Advance(TimeSpan.FromSeconds(3));
        _source.RaiseDevicesChanged();
        _time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal((0, 0, true), (pushes, _source.PairedQueries, _source.Disposed));
    }
}
