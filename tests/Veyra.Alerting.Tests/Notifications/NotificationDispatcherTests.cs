using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Notifications;
using Veyra.Core.Alerts;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.Notifications;

[Collection(PostgresGroup.Name)]
public sealed class NotificationDispatcherTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(_noon);
    private readonly FakeWebhooks _webhooks = new();
    private readonly EphemeralDataProtectionProvider _keys = new();

    [Fact]
    public async Task DispatchAsync_DueMessage_IsPostedWithALinkAndMarkedSent()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        await AddWebhookAsync(database);
        var alert = await OpenAsync(database);

        var tried = await Dispatcher(database).DispatchAsync(CancellationToken.None);

        Assert.Equal(1, tried);
        using var json = JsonDocument.Parse(Assert.Single(_webhooks.Received).Body);
        Assert.Equal("opened", json.RootElement.GetProperty("event").GetString());
        Assert.Equal(
            $"https://veyra.example.com/alerts/{alert.Id}",
            json.RootElement.GetProperty("alert").GetProperty("url").GetString());
        var delivery = Assert.Single(await new DeliveryStore(database).ListForAlertAsync(alert.Id, default));
        Assert.Equal((DeliveryState.Sent, 204, 1), (delivery.State, delivery.LastStatus, delivery.Attempts));
    }

    [Fact]
    public async Task DispatchAsync_Failure_RetriesWithGrowingWaitsThenGivesUp()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        await AddWebhookAsync(database);
        var alert = await OpenAsync(database);
        var dispatcher = Dispatcher(database);
        var store = new DeliveryStore(database);
        _webhooks.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));

        foreach (var wait in NotificationDispatcher.RetryDelays)
        {
            Assert.Equal(1, await dispatcher.DispatchAsync(CancellationToken.None));
            var waiting = Assert.Single(await store.ListForAlertAsync(alert.Id, default));
            Assert.Equal(DeliveryState.Pending, waiting.State);
            Assert.Equal(_clock.GetUtcNow() + wait, waiting.NextAttemptAt);

            // Not due a moment early, due right on time.
            _clock.Advance(wait - TimeSpan.FromSeconds(1));
            Assert.Equal(0, await dispatcher.DispatchAsync(CancellationToken.None));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(1, await dispatcher.DispatchAsync(CancellationToken.None));

        var failed = Assert.Single(await store.ListForAlertAsync(alert.Id, default));
        Assert.Equal(
            (DeliveryState.Failed, 6, "HTTP 502 Bad Gateway"),
            (failed.State, failed.Attempts, failed.LastError));
        Assert.Equal(6, _webhooks.Received.Count);
    }

    [Fact]
    public async Task DispatchAsync_ChannelSwitchedOffAfterQueueing_FailsWithoutSending()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var channel = await AddWebhookAsync(database);
        var alert = await OpenAsync(database);
        await new ChannelStore(database).UpdateAsync(channel with { Enabled = false }, CancellationToken.None);

        await Dispatcher(database).DispatchAsync(CancellationToken.None);

        Assert.Empty(_webhooks.Received);
        var delivery = Assert.Single(await new DeliveryStore(database).ListForAlertAsync(alert.Id, default));
        Assert.Equal(DeliveryState.Failed, delivery.State);
        Assert.Contains("switched off", delivery.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchAsync_UnreadableUrl_FailsWithAdvice()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        await new ChannelStore(database).InsertAsync(Channels.Webhook("ops"), CancellationToken.None);
        var alert = await OpenAsync(database);
        var dispatcher = Dispatcher(database);

        await dispatcher.DispatchAsync(CancellationToken.None);

        var delivery = Assert.Single(await new DeliveryStore(database).ListForAlertAsync(alert.Id, default));
        Assert.Contains("Enter the URL again", delivery.LastError, StringComparison.Ordinal);
        Assert.Empty(_webhooks.Received);
    }

    [Fact]
    public async Task DispatchAsync_LimitsOneRunToTheBatchSize()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        await AddWebhookAsync(database);

        foreach (var service in new[] { "a", "b", "c" })
        {
            await OpenAsync(database, service);
        }

        var dispatcher = Dispatcher(database, batchSize: 2);

        Assert.Equal(2, await dispatcher.DispatchAsync(CancellationToken.None));
        Assert.Equal(1, await dispatcher.DispatchAsync(CancellationToken.None));
        Assert.Equal(0, await dispatcher.DispatchAsync(CancellationToken.None));
    }

    public void Dispose() => _webhooks.Dispose();

    private async Task<NotificationChannel> AddWebhookAsync(NpgsqlDataSource database)
    {
        var url = new Uri("https://hooks.example.com/ops");
        var protectedUrl = new WebhookUrlProtector(_keys).Protect(url);
        var channel = Channels.Webhook("ops") with { WebhookUrlProtected = protectedUrl };

        return await new ChannelStore(database).InsertAsync(channel, CancellationToken.None);
    }

    private async Task<AlertState> OpenAsync(NpgsqlDataSource database, string service = "payments-gateway")
    {
        var now = _clock.GetUtcNow();
        var alert = new AlertState(
            Guid.CreateVersion7(),
            "error-rate",
            "production",
            AlertSubject.Service(service),
            AlertSeverity.Warning,
            0.2,
            0.2,
            0.05,
            TimeSpan.FromMinutes(5),
            now,
            now,
            null);

        await new AlertStore(database).InsertAsync(alert, CancellationToken.None);

        return alert;
    }

    private NotificationDispatcher Dispatcher(NpgsqlDataSource database, int batchSize = 10)
    {
        var options = Options.Create(new AlertingOptions
        {
            PublicUrl = "https://veyra.example.com",
            Notifications = new NotificationOptions { BatchSize = batchSize },
        });
        var sender = new ChannelSender(
            new WebhookSender(_webhooks, options),
            new EmailSender(options),
            new WebhookUrlProtector(_keys));

        return new NotificationDispatcher(
            new DeliveryStore(database),
            sender,
            options,
            _clock,
            NullLogger<NotificationDispatcher>.Instance);
    }
}
