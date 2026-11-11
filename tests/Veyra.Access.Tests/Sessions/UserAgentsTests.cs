using Veyra.Access.Sessions;

namespace Veyra.Access.Tests.Sessions;

public class UserAgentsTests
{
    [Theory]
    [InlineData(
        "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:132.0) Gecko/20100101 Firefox/132.0",
        "Firefox on Ubuntu")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 "
            + "Safari/537.36",
        "Chrome on Windows")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 "
            + "Safari/537.36 Edg/131.0.0.0",
        "Edge on Windows")]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) "
            + "Version/18.1 Mobile/15E148 Safari/604.1",
        "Safari on iPhone")]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 "
            + "Safari/605.1.15",
        "Safari on macOS")]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 "
            + "Mobile Safari/537.36",
        "Chrome on Android")]
    [InlineData("curl/8.10.1", "curl")]
    public void Describe_KnownBrowsers_NamesBrowserAndSystem(string userAgent, string expected)
    {
        Assert.Equal(expected, UserAgents.Describe(userAgent));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Describe_Missing_SaysUnknown(string? userAgent)
    {
        Assert.Equal("Unknown browser", UserAgents.Describe(userAgent));
    }

    [Fact]
    public void Trim_LongHeader_IsCutTo512Characters()
    {
        Assert.Equal(512, UserAgents.Trim(new string('a', 2000))!.Length);
    }
}
