using BatteryHub.Core;

namespace BatteryHub.Tests.Monitor;

public class ReadingDeduplicatorTests
{
    private static BatteryReading Reading(string id, string source, int? percent = null, CoarseLevel? coarse = null, ReadingStatus status = ReadingStatus.Ok) => new()
    {
        DeviceId = id,
        Name = id,
        Kind = DeviceKind.Headset,
        Connection = ConnectionType.Bluetooth,
        Percent = percent,
        CoarseLevel = coarse,
        Status = status,
        Timestamp = DateTimeOffset.UnixEpoch,
        Source = source,
    };

    [Fact]
    public void Different_devices_are_all_kept_in_first_seen_order()
    {
        var merged = ReadingDeduplicator.Merge([[Reading("b", "one", 10)], [Reading("a", "two", 20)]]);

        Assert.Equal(["b", "a"], merged.Select(r => r.DeviceId));
    }

    [Fact]
    public void The_more_informative_reading_wins_whichever_provider_comes_first()
    {
        var noData = Reading("bt-1", "first", status: ReadingStatus.NoData);
        var coarse = Reading("bt-1", "second", coarse: CoarseLevel.Low);
        var percent = Reading("bt-1", "third", percent: 40);

        Assert.Equal("third", ReadingDeduplicator.Merge([[noData], [coarse], [percent]]).Single().Source);
        Assert.Equal("third", ReadingDeduplicator.Merge([[percent], [coarse], [noData]]).Single().Source);
        Assert.Equal("second", ReadingDeduplicator.Merge([[noData], [coarse]]).Single().Source);
    }

    [Fact]
    public void Ties_go_to_the_provider_listed_first()
    {
        var merged = ReadingDeduplicator.Merge([[Reading("bt-1", "first", 40)], [Reading("bt-1", "second", 90)]]);

        Assert.Equal("first", merged.Single().Source);
    }

    [Fact]
    public void Ranks_follow_how_much_the_user_learns()
    {
        int[] ranks =
        [
            ReadingDeduplicator.Rank(Reading("x", "s", percent: 50)),
            ReadingDeduplicator.Rank(Reading("x", "s", coarse: CoarseLevel.Full)),
            ReadingDeduplicator.Rank(Reading("x", "s", status: ReadingStatus.DeviceError)),
            ReadingDeduplicator.Rank(Reading("x", "s", status: ReadingStatus.InUseByAnotherApp)),
            ReadingDeduplicator.Rank(Reading("x", "s", status: ReadingStatus.NoData)),
        ];

        Assert.Equal(ranks.OrderDescending(), ranks);
        Assert.Equal(ranks.Length, ranks.Distinct().Count());
    }

    [Theory]
    [InlineData("a0:ab:51:c0:ff:ee", 0xA0AB51C0FFEEUL)]
    [InlineData("A0-AB-51-C0-FF-EE", 0xA0AB51C0FFEEUL)]
    [InlineData("a0ab51c0ffee", 0xA0AB51C0FFEEUL)]
    [InlineData(" a0:ab:51:c0:ff:ee ", 0xA0AB51C0FFEEUL)]
    public void Parses_windows_address_strings(string text, ulong expected)
    {
        Assert.True(BluetoothAddress.TryParse(text, out ulong address));
        Assert.Equal(expected, address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a0:ab:51:c0:ff")]
    [InlineData("a0:ab:51:c0:ff:ee:01")]
    [InlineData("g0:ab:51:c0:ff:ee")]
    [InlineData("+0ab51c0ffee")]
    [InlineData("00:00:00:00:00:00")] // the PC's own services
    [InlineData("000000000000")]
    [InlineData("FFFFFFFFFFFF")] // broadcast, names no device
    public void Rejects_anything_that_is_not_a_bluetooth_address(string? text)
    {
        Assert.False(BluetoothAddress.TryParse(text, out _));
    }

    private static readonly Guid Headset = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    [Fact]
    public void A_dual_mode_device_is_keyed_by_its_classic_address()
    {
        (ulong, Guid?)[] classic = [(0x111111111111, Guid.NewGuid()), (0xA0AB51C0FFEE, Headset)];

        Assert.Equal(0xA0AB51C0FFEEUL, BluetoothIdentity.ClassicAddressInContainer(Headset, classic));
    }

    [Fact]
    public void Without_one_classic_device_in_the_container_there_is_no_classic_address()
    {
        Assert.Null(BluetoothIdentity.ClassicAddressInContainer(Headset, [(0x111111111111, Guid.NewGuid()), (0x222222222222, null)]));
        Assert.Null(BluetoothIdentity.ClassicAddressInContainer(Headset, [(0x111111111111, Headset), (0x222222222222, Headset)]));
        Assert.Null(BluetoothIdentity.ClassicAddressInContainer(null, [(0x111111111111, null)]));
        Assert.Equal(0x111111111111UL, BluetoothIdentity.ClassicAddressInContainer(Headset, [(0x111111111111, Headset), (0x111111111111, Headset)]));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000-0000-0000-ffff-ffffffffffff")] // the PC's own container
    public void Containers_that_name_no_device_are_never_joined(string container)
    {
        var id = Guid.Parse(container);

        Assert.Null(BluetoothIdentity.ClassicAddressInContainer(id, [(0x111111111111, id)]));
    }

    [Fact]
    public void Bluetooth_ids_are_twelve_lowercase_hex_digits()
    {
        Assert.Equal("bt-a0ab51c0ffee", DeviceIds.Bluetooth(0xA0AB51C0FFEE));
        Assert.Equal("bt-00000000000a", DeviceIds.Bluetooth(0xA));
        Assert.Equal("bt-a0ab51c0ffee", DeviceIds.Bluetooth(0xFFFF_A0AB51C0FFEE));
    }
}
