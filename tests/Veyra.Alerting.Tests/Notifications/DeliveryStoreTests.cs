using Npgsql;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Notifications;
using Veyra.Core.Alerts;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.Notifications;

[Collection(PostgresGroup.Name)]
public class DeliveryStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaimDueAsync_ReturnsTheEventAlertAndChannelAndLeasesTheDelivery()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var channel = await new ChannelStore(database).InsertAsync(Channels.Webhook("ops"), CancellationToken.None);
        var alert = await OpenAsync(database);
        var store = new DeliveryStore(database);

        var claimed = Assert.Single(await store.ClaimDueAsync(_noon, 10, _noon.AddMinutes(5), default));
        var again = await store.ClaimDueAsync(_noon.AddMinutes(1), 10, _noon.AddMinutes(6), default);

        Assert.Equal(1, claimed.Attempt);
        Assert.Equal(AlertEventKind.Opened, claimed.Event.Kind);
        Assert.Equal(alert, claimed.Alert);
        Assert.Equal(channel, claimed.Channel);
        Assert.Empty(again);
        var leased = Assert.Single(await store.ListForAlertAsync(alert.Id, default));
        Assert.Equal(_noon.AddMinutes(5), leased.NextAttemptAt);
    }

    [Fact]
    public async Task ClaimDueAsync_NotDueYet_ClaimsNothing()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        await new ChannelStore(database).InsertAsync(Channels.Webhook("ops"), CancellationToken.None);
        await OpenAsync(database);

        var claimed = await new DeliveryStore(database).ClaimDueAsync(_noon.AddSeconds(-1), 10, _noon, default);

        Assert.Empty(claimed);
    }

    [Fact]
    public async Task MarkSentAsync_SettlesTheDelivery()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        await new ChannelStore(database).InsertAsync(Channels.Webhook("ops"), CancellationToken.None);
        var alert = await OpenAsync(database);
        var store = new DeliveryStore(database);
        var claimed = Assert.Single(await store.ClaimDueAsync(_noon, 10, _noon.AddMinutes(5), default));

        await store.MarkSentAsync(claimed.Id, 204, _noon.AddSeconds(1), CancellationToken.None);

        var delivery = Assert.Single(await store.ListForAlertAsync(alert.Id, default));
        Assert.Equal(DeliveryState.Sent, delivery.State);
        Assert.Equal(204, delivery.LastStatus);
        Assert.Equal(_noon.AddSeconds(1), delivery.SentAt);
        Assert.Empty(await store.ClaimDueAsync(_noon.AddHours(1), 10, _noon.AddHours(2), default));
    }

    [Fact]
    public async Task MarkFailedAsync_WithNextTry_WaitsAndIsClaimedAgainThen()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        await new ChannelStore(database).InsertAsync(Channels.Webhook("ops"), CancellationToken.None);
        var alert = await OpenAsync(database);
        var store = new DeliveryStore(database);
        var claimed = Assert.Single(await store.ClaimDueAsync(_noon, 10, _noon.AddMinutes(5), default));

        await store.MarkFailedAsync(claimed.Id, "HTTP 503", 503, _noon, _noon.AddSeconds(30), default);

        var waiting = Assert.Single(await store.ListForAlertAsync(alert.Id, default));
        Assert.Equal((DeliveryState.Pending, "HTTP 503", 503), (waiting.State, waiting.LastError, waiting.LastStatus));
        Assert.Empty(await store.ClaimDueAsync(_noon.AddSeconds(29), 10, _noon.AddMinutes(5), default));
        var retried = Assert.Single(await store.ClaimDueAsync(_noon.AddSeconds(30), 10, _noon.AddMinutes(5), default));
        Assert.Equal(2, retried.Attempt);
    }

    [Fact]
    public async Task MarkFailedAsync_WithoutNextTry_FailsForGood()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        await new ChannelStore(database).InsertAsync(Channels.Webhook("ops"), CancellationToken.None);
        var alert = await OpenAsync(database);
        var store = new DeliveryStore(database);
        var claimed = Assert.Single(await store.ClaimDueAsync(_noon, 10, _noon.AddMinutes(5), default));

        await store.MarkFailedAsync(claimed.Id, "Timed out after 10 s", null, _noon, null, default);

        Assert.Equal(DeliveryState.Failed, Assert.Single(await store.ListForAlertAsync(alert.Id, default)).State);
        Assert.Empty(await store.ClaimDueAsync(_noon.AddDays(1), 10, _noon.AddDays(2), default));
    }

    [Fact]
    public async Task GetActivityAsync_CountsPerDayAndReportsTheLatestTry()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var channel = await new ChannelStore(database).InsertAsync(Channels.Webhook("ops"), CancellationToken.None);
        var store = new DeliveryStore(database);
        var first = await OpenAsync(database, "orders");
        var second = await OpenAsync(database, "payments");
        var claimed = await store.ClaimDueAsync(_noon, 10, _noon.AddMinutes(5), default);
        await store.MarkSentAsync(claimed[0].Id, 200, _noon.AddDays(-1), default);
        await store.MarkFailedAsync(claimed[1].Id, "HTTP 500", 500, _noon, _noon.AddMinutes(1), default);

        var activity = (await store.GetActivityAsync(DateOnly.FromDateTime(_noon.UtcDateTime), 3, default))[channel.Id];

        Assert.Equal(
            [
                new DayActivity(new DateOnly(2026, 11, 7), 0, 0),
                new DayActivity(new DateOnly(2026, 11, 8), 1, 0),
                new DayActivity(new DateOnly(2026, 11, 9), 0, 1),
            ],
            activity.Days);
        Assert.Equal((_noon, "HTTP 500", 500), (activity.LastAttemptAt, activity.LastError, activity.LastStatus));
        Assert.Equal(_noon.AddMinutes(1), activity.NextAttemptAt);
        Assert.NotEqual(first.Id, second.Id);
    }

    private static async Task<AlertState> OpenAsync(NpgsqlDataSource database, string service = "orders")
    {
        var alert = new AlertState(
            Id: Guid.CreateVersion7(),
            Rule: "error-rate",
            Environment: "production",
            Subject: AlertSubject.Service(service),
            Severity: AlertSeverity.Warning,
            Value: 0.2,
            Peak: 0.2,
            Threshold: 0.05,
            Window: TimeSpan.FromMinutes(5),
            OpenedAt: _noon,
            UpdatedAt: _noon,
            ResolvedAt: null);

        await new AlertStore(database).InsertAsync(alert, CancellationToken.None);

        return alert;
    }
}
