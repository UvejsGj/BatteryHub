using System.Globalization;
using BatteryHub.Core;
using BatteryHub.Core.Sony;
using BatteryHub.Tests.Fixtures;

namespace BatteryHub.Tests.Sony;

/// <summary>Runs every report under Fixtures/Sony through the parser its headers name.</summary>
public class SonyFixtureTests
{
    public static TheoryData<string> Files => Fixture.All("Sony");

    [Theory]
    [MemberData(nameof(Files))]
    public void Parses_as_expected(string file)
    {
        var fixture = Fixture.Load(file);
        var model = SonyPadModel.Find(0x054C, int.Parse(fixture["product-id"], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture))
            ?? throw new InvalidOperationException($"{file}: unknown product-id");
        var connection = Enum.Parse<ConnectionType>(fixture["connection"], ignoreCase: true);
        string expect = fixture["expect"];

        bool parsed = model.TryParse(fixture.Bytes, connection, out var status);

        if (expect == "reject")
        {
            Assert.False(parsed);
            return;
        }

        Assert.True(parsed);
        if (expect == "fault")
        {
            Assert.Null(status.Percent);
            Assert.NotNull(status.Fault);
            return;
        }

        string[] parts = expect.Split(' ');
        Assert.Equal(int.Parse(parts[0].TrimEnd('%'), CultureInfo.InvariantCulture), status.Percent);
        Assert.Equal(Enum.Parse<ChargeState>(parts[1]), status.ChargeState);
        Assert.Null(status.Fault);
    }

    [Fact]
    public void Finds_the_fixtures()
    {
        Assert.NotEmpty(Files);
    }
}
