using System.Net;
using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Tests.Notifications;

/// <summary>Answers webhook posts the way a test says, and keeps every request it received.</summary>
internal sealed class FakeWebhooks : HttpMessageHandler, IHttpClientFactory
{
    /// <summary>The requests received, with their bodies read.</summary>
    public List<(HttpRequestMessage Request, string Body)> Received { get; } = [];

    /// <summary>What to answer. Defaults to 204 No Content.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

    /// <summary>Hands out clients that send through this handler, under any name.</summary>
    public HttpClient CreateClient(string name)
    {
        Assert.Equal(WebhookSender.ClientName, name);

        return new HttpClient(this, disposeHandler: false);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Received.Add((request, body));

        return await Answer(request, cancellationToken);
    }
}
