using Veyra.Core.Time;

namespace Veyra.Core.Tests.Time;

public class TimeZonesTests
{
    [Theory]
    [InlineData("Europe/Berlin")]
    [InlineData("America/New_York")]
    [InlineData("UTC")]
    public void TryFind_IanaName_FindsTheZone(string name)
    {
        Assert.True(TimeZones.TryFind(name, out var zone));
        Assert.NotNull(zone);
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("")]
    [InlineData("W. Europe Standard Time")]
    public void TryFind_UnknownOrWindowsName_FindsNothing(string name)
    {
        Assert.False(TimeZones.TryFind(name, out _));
    }

    [Fact]
    public void LocalMidnight_DaysBack_IsMidnightInTheZoneAsAnInstant()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var now = new DateTimeOffset(2026, 11, 3, 7, 34, 0, TimeSpan.Zero);

        var start = TimeZones.LocalMidnight(now, zone, daysBack: 2);

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero), start);
    }
}
