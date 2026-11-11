using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Tests.Notifications;

public sealed class WebhookSenderTests : IDisposable
{
    private static readonly Uri _hook = new("https://hooks.example.com/services/abc");

    private readonly FakeWebhooks _webhooks = new();

    [Fact]
    public async Task SendAsync_PostsTheMessageAsJson()
    {
        var result = await Sender().SendAsync(_hook, NotificationMessages.Test("ops"), CancellationToken.None);

        Assert.Equal(new SendResult(true, 204, null), result);
        var (request, body) = Assert.Single(_webhooks.Received);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(_hook, request.RequestUri);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("test", JsonDocument.Parse(body).RootElement.GetProperty("event").GetString());
        Assert.Contains("Veyra", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ErrorStatus_FailsWithTheStatus()
    {
        _webhooks.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await Sender().SendAsync(_hook, NotificationMessages.Test("ops"), CancellationToken.None);

        Assert.Equal(new SendResult(false, 503, "HTTP 503 Service Unavailable"), result);
    }

    [Fact]
    public async Task SendAsync_Redirect_IsNotFollowedAndFails()
    {
        _webhooks.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found));

        var result = await Sender().SendAsync(_hook, NotificationMessages.Test("ops"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(302, result.Status);
    }

    [Fact]
    public async Task SendAsync_SlowReceiver_TimesOut()
    {
        _webhooks.Answer = async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);

            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var result = await Sender(TimeSpan.FromMilliseconds(100))
            .SendAsync(_hook, NotificationMessages.Test("ops"), CancellationToken.None);

        Assert.Equal(SendResult.Failed("Timed out after 0.1 s"), result);
    }

    [Fact]
    public async Task SendAsync_Unreachable_FailsWithTheReason()
    {
        _webhooks.Answer = (_, _) => throw new HttpRequestException("No such host is known.");

        var result = await Sender().SendAsync(_hook, NotificationMessages.Test("ops"), CancellationToken.None);

        Assert.Equal(SendResult.Failed("Could not reach the webhook: No such host is known."), result);
    }

    public void Dispose() => _webhooks.Dispose();

    private WebhookSender Sender(TimeSpan? timeout = null) => new(
        _webhooks,
        Options.Create(new AlertingOptions
        {
            Notifications = new NotificationOptions { SendTimeout = timeout ?? TimeSpan.FromSeconds(10) },
        }));
}
