using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Tests.Notifications;

public class WebhookUrlsTests
{
    [Theory]
    [InlineData("https://hooks.slack.com/services/T0A1/B0C2/x9Y")]
    [InlineData("http://localhost:9000/hook")]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://[::1]:8080/hook")]
    public void Parse_HttpsOrThisMachine_IsAccepted(string url)
    {
        Assert.Equal(new Uri(url), WebhookUrls.Parse(url));
    }

    [Theory]
    [InlineData("", "Enter the webhook URL.")]
    [InlineData("hooks.example.com/x", "full http or https")]
    [InlineData("ftp://hooks.example.com/x", "full http or https")]
    [InlineData("http://hooks.example.com/x", "must use https")]
    [InlineData("https://user:secret@hooks.example.com/x", "credentials")]
    public void Parse_BrokenRule_SaysWhich(string url, string expected)
    {
        var thrown = Assert.Throws<AlertingValidationException>(() => WebhookUrls.Parse(url));

        Assert.Contains(expected, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TooLong_IsRefused()
    {
        var url = "https://h.example.com/" + new string('a', 2000);

        Assert.Throws<AlertingValidationException>(() => WebhookUrls.Parse(url));
    }

    [Fact]
    public void Hint_KeepsSchemeHostAndTheStartOfThePath()
    {
        var hint = WebhookUrls.Hint(new Uri("https://hooks.slack.com/services/T0A1/B0C2/x9Y?token=abc"));

        Assert.Equal("https://hooks.slack.com/services/T0…", hint);
    }

    [Fact]
    public void Hint_ShortPath_IsShownWhole()
    {
        Assert.Equal("http://localhost:9000/hook", WebhookUrls.Hint(new Uri("http://localhost:9000/hook")));
    }
}
