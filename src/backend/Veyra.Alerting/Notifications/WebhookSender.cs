using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Veyra.Alerting.Notifications;

/// <summary>Posts notifications as JSON to webhook URLs.</summary>
/// <param name="clients">Provides the <see cref="ClientName"/> client, which follows no redirects.</param>
/// <param name="options">The Alerting settings with the time limit per try.</param>
internal sealed class WebhookSender(IHttpClientFactory clients, IOptions<AlertingOptions> options)
{
    /// <summary>The name of the HTTP client registered for webhooks.</summary>
    public const string ClientName = "Veyra.Webhooks";

    /// <summary>Sends one message. A 2xx response counts as delivered; the response body is ignored.</summary>
    /// <param name="url">Where to post.</param>
    /// <param name="message">What to post.</param>
    /// <param name="cancellationToken">Cancels the try when Veyra stops.</param>
    /// <returns>How the try ended. Never throws for network or HTTP failures.</returns>
    public async Task<SendResult> SendAsync(Uri url, NotificationMessage message, CancellationToken cancellationToken)
    {
        var timeout = options.Value.Notifications.SendTimeout;

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);

        using var content = new StringContent(NotificationMessages.ToJson(message), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Veyra", "0.1"));

        try
        {
            using var client = clients.CreateClient(ClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                limit.Token);
            var status = (int)response.StatusCode;

            return response.IsSuccessStatusCode
                ? new SendResult(true, status, null)
                : new SendResult(false, status, $"HTTP {status} {response.ReasonPhrase}".TrimEnd());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SendResult.Failed(
                string.Create(CultureInfo.InvariantCulture, $"Timed out after {timeout.TotalSeconds:0.#} s"));
        }
        catch (HttpRequestException exception)
        {
            return SendResult.Failed($"Could not reach the webhook: {exception.Message}");
        }
    }
}
