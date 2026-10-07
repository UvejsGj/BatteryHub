using BatteryHub.Core;

namespace BatteryHub.Tests;

public class BatteryReadingTests
{
    private static BatteryReading Reading(int? percent) => new()
    {
        DeviceId = "id",
        Name = "Pad",
        Kind = DeviceKind.Gamepad,
        Connection = ConnectionType.Usb,
        Timestamp = DateTimeOffset.UnixEpoch,
        Source = "test",
        Percent = percent,
    };

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(100)]
    public void Accepts_percent_in_range(int? percent)
    {
        Assert.Equal(percent, Reading(percent).Percent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_percent_out_of_range(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Reading(percent));
    }
}
