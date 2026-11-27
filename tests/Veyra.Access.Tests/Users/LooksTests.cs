using Veyra.Access.Users;

namespace Veyra.Access.Tests.Users;

public sealed class LooksTests
{
    [Theory]
    [InlineData("forecast")]
    [InlineData("console")]
    public void IsKnown_LookOfTheApp_IsTrue(string look)
    {
        Assert.True(Looks.IsKnown(look));
    }

    [Theory]
    [InlineData("city")]
    [InlineData("Console")]
    [InlineData("")]
    [InlineData(null)]
    public void IsKnown_AnythingElse_IsFalse(string? look)
    {
        Assert.False(Looks.IsKnown(look));
    }

    [Fact]
    public void All_ForecastAndConsole_InTheOrderTheyAreOffered()
    {
        Assert.Equal(["forecast", "console"], Looks.All);
    }

    [Fact]
    public void Unknown_ListsEveryLook()
    {
        Assert.Equal("The look must be one of: forecast, console.", Looks.Unknown);
    }
}
