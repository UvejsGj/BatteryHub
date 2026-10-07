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

    private BluetoothPropertyProvider Provider() => new(_source, _time);

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

    [Fact]
    public async Task Never_shows_the_level_Windows_kept_after_a_disconnect()
    {
        _source.Nodes.Add(HandsFreeNode(1));
        _source.Endpoints.Add(Classic(connected: false));
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
    public async Task Falls_back_to_the_container_when_the_addresses_differ()
    {
        var container = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        _source.Nodes.Add(LowEnergyNode(64, address: 0x5A5A5A5A5A5A, container: container));
        _source.Endpoints.Add(Le(connected: true, container: Guid.NewGuid()));
        _source.Endpoints.Add(Le(connected: true, address: 0x6B6B6B6B6B6B, container: container));
        using var provider = Provider();

        var reading = Assert.Single(await provider.PollAsync(Ct));

        Assert.Equal(("bt-5a5a5a5a5a5a", (int?)64), (reading.DeviceId, reading.Percent));
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
    public void Polls_every_minute_without_push_events()
    {
        using var provider = Provider();
        provider.ReadingsChanged += (_, _) => Assert.Fail("never raised");

        Assert.Equal(TimeSpan.FromSeconds(60), provider.PollInterval);
        Assert.Equal("bt-property", provider.Name);
    }
}
