using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Veyra.Alerting.Tests.Notifications;

/// <summary>
/// Starts one throwaway Mailpit container, a real SMTP server that keeps what it receives and shows it over HTTP,
/// so email tests check what actually went over the wire.
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int _smtpPort = 1025;

    private const int _apiPort = 8025;

    // Pinned, so a new Mailpit release cannot change the tests' behaviour unnoticed.
    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:v1.32.0")
        .WithPortBinding(_smtpPort, assignRandomHostPort: true)
        .WithPortBinding(_apiPort, assignRandomHostPort: true)

        // Accept any user name and password over plain SMTP, so the sign-in path can be tested without TLS.
        .WithEnvironment("MP_SMTP_AUTH_ACCEPT_ANY", "1")
        .WithEnvironment("MP_SMTP_AUTH_ALLOW_INSECURE", "1")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(_apiPort).ForPath("/readyz")))
        .Build();

    private Uri? _api;

    /// <summary>The host name to send mail to.</summary>
    public string Host => _container.Hostname;

    /// <summary>The SMTP port on the host.</summary>
    public int SmtpPort => _container.GetMappedPublicPort(_smtpPort);

    /// <summary>Starts the container.</summary>
    /// <returns>A task that completes when Mailpit is ready.</returns>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _api = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(_apiPort)}/");
    }

    /// <summary>Stops and removes the container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Returns the emails sent to one address, newest first, with their plain-text bodies.</summary>
    /// <param name="recipient">The recipient's address.</param>
    /// <returns>Subject, sender, every recipient, and body of each email.</returns>
    public async Task<IReadOnlyList<ReceivedEmail>> ReceivedByAsync(string recipient)
    {
        var address = _api ?? throw new InvalidOperationException("Mailpit has not started.");
        using var api = new HttpClient { BaseAddress = address };
        var list = await api.GetFromJsonAsync<MessageList>($"api/v1/search?query=to:{Uri.EscapeDataString(recipient)}");
        var emails = new List<ReceivedEmail>();

        foreach (var summary in list?.Messages ?? [])
        {
            var full = await api.GetFromJsonAsync<Message>($"api/v1/message/{summary.Id}");

            emails.Add(new ReceivedEmail(
                summary.Subject,
                summary.From.Address,
                summary.To.Select(to => to.Address).ToList(),
                full?.Text ?? string.Empty));
        }

        return emails;
    }

    private sealed record MessageList([property: JsonPropertyName("messages")] IReadOnlyList<MessageSummary> Messages);

    private sealed record MessageSummary(
        [property: JsonPropertyName("ID")] string Id,
        [property: JsonPropertyName("Subject")] string Subject,
        [property: JsonPropertyName("From")] Mailbox From,
        [property: JsonPropertyName("To")] IReadOnlyList<Mailbox> To);

    private sealed record Mailbox([property: JsonPropertyName("Address")] string Address);

    private sealed record Message([property: JsonPropertyName("Text")] string Text);
}

/// <summary>An email as Mailpit received it.</summary>
/// <param name="Subject">The subject.</param>
/// <param name="From">The sender address.</param>
/// <param name="To">The recipient addresses.</param>
/// <param name="Text">The plain-text body.</param>
public sealed record ReceivedEmail(string Subject, string From, IReadOnlyList<string> To, string Text);

/// <summary>Shares one Mailpit container across the email tests.</summary>
[CollectionDefinition(Name)]
public sealed class MailpitGroup : ICollectionFixture<MailpitFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "Mailpit";
}
