using Veyra.Alerting.Alerts;
using Veyra.Alerting.Notifications;
using Veyra.Alerting.Tests.Notifications;
using Veyra.Core.Alerts;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.Alerts;

[Collection(PostgresGroup.Name)]
public class AlertStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InsertAsync_ThenListOpen_ReturnsEveryField()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var alert = Alert("payments-gateway");

        await store.InsertAsync(alert, CancellationToken.None);
        var open = await store.ListOpenAsync("error-rate", CancellationToken.None);

        var stored = Assert.Single(open);
        Assert.Equal(alert, stored);
    }

    [Fact]
    public async Task InsertAsync_SecondOpenAlertForSameService_Throws()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        await store.InsertAsync(Alert("orders"), CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(
            () => store.InsertAsync(Alert("orders") with { Id = Guid.CreateVersion7() }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RaisesPeakButNeverLowersIt()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var alert = Alert("orders");
        await store.InsertAsync(alert, CancellationToken.None);

        await store.UpdateAsync(alert.Id, AlertSeverity.Warning, 0.2, 0.05, _noon.AddMinutes(1), CancellationToken.None);
        await store.UpdateAsync(alert.Id, AlertSeverity.Warning, 0.04, 0.05, _noon.AddMinutes(2), CancellationToken.None);

        var stored = Assert.Single(await store.ListOpenAsync("error-rate", CancellationToken.None));
        Assert.Equal(0.04, stored.Value);
        Assert.Equal(0.2, stored.Peak);
        Assert.Equal(_noon.AddMinutes(2), stored.UpdatedAt);
        Assert.Equal(alert.OpenedAt, stored.OpenedAt);
    }

    [Fact]
    public async Task ResolveAsync_RemovesFromOpenAndAllowsANewAlert()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var alert = Alert("orders");
        await store.InsertAsync(alert, CancellationToken.None);

        await store.ResolveAsync(alert.Id, 0.001, _noon.AddMinutes(10), CancellationToken.None);
        await store.InsertAsync(Alert("orders") with { Id = Guid.CreateVersion7() }, CancellationToken.None);

        Assert.Single(await store.ListOpenAsync("error-rate", CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_ReturnsOpenAndRecentlyResolved()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var open = Alert("open");
        var recent = Alert("recent");
        var old = Alert("old");
        await store.InsertAsync(open, CancellationToken.None);
        await store.InsertAsync(recent, CancellationToken.None);
        await store.InsertAsync(old, CancellationToken.None);
        await store.ResolveAsync(recent.Id, 0, _noon.AddHours(-1), CancellationToken.None);
        await store.ResolveAsync(old.Id, 0, _noon.AddDays(-2), CancellationToken.None);

        var alerts = await store.ListAsync(_noon.AddDays(-1), CancellationToken.None);

        Assert.Equal(["open", "recent"], alerts.Select(a => a.Subject.Name).Order());
        Assert.Equal(_noon.AddHours(-1), alerts.Single(a => a.Subject.Name == "recent").ResolvedAt);
    }

    [Fact]
    public async Task DeleteResolvedBeforeAsync_KeepsOpenAndRecentAlerts()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var open = Alert("open");
        var old = Alert("old");
        await store.InsertAsync(open, CancellationToken.None);
        await store.InsertAsync(old, CancellationToken.None);
        await store.ResolveAsync(old.Id, 0, _noon.AddDays(-40), CancellationToken.None);

        var deleted = await store.DeleteResolvedBeforeAsync(_noon.AddDays(-30), CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(["open"], (await store.ListAsync(DateTimeOffset.MinValue, CancellationToken.None)).Select(a => a.Subject.Name));
    }

    [Fact]
    public async Task InsertAsync_RecordsOpenedAndQueuesDeliveriesForChannelsTakingTheSeverity()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var channels = new ChannelStore(database);
        var cautions = await channels.InsertAsync(Channels.Webhook("cautions"), CancellationToken.None);
        var warnings = await channels.InsertAsync(
            Channels.Webhook("warnings only") with { SendCaution = false },
            CancellationToken.None);
        await channels.InsertAsync(Channels.Webhook("off") with { Enabled = false }, CancellationToken.None);
        var alert = Alert("orders");

        await store.InsertAsync(alert, CancellationToken.None);

        var opened = Assert.Single(await store.ListEventsAsync(alert.Id, CancellationToken.None));
        Assert.Equal(AlertEventKind.Opened, opened.Kind);
        Assert.Equal(AlertSeverity.Caution, opened.Severity);
        Assert.Equal(0.02, opened.Value);
        Assert.Equal(_noon, opened.At);
        var delivery = Assert.Single(await new DeliveryStore(database).ListForAlertAsync(alert.Id, default));
        Assert.Equal(cautions.Id, delivery.ChannelId);
        Assert.Equal(opened.Id, delivery.EventId);
        Assert.Equal(DeliveryState.Pending, delivery.State);
        Assert.Equal(_noon, delivery.NextAttemptAt);
        Assert.NotEqual(warnings.Id, delivery.ChannelId);
    }

    [Fact]
    public async Task UpdateAsync_SeverityRises_RecordsEscalationAndQueuesWarningDeliveries()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var warnings = await new ChannelStore(database).InsertAsync(
            Channels.Webhook("warnings only") with { SendCaution = false },
            CancellationToken.None);
        var alert = Alert("orders");
        await store.InsertAsync(alert, CancellationToken.None);

        await store.UpdateAsync(alert.Id, AlertSeverity.Warning, 0.2, 0.05, _noon.AddMinutes(1), CancellationToken.None);
        await store.UpdateAsync(alert.Id, AlertSeverity.Warning, 0.3, 0.05, _noon.AddMinutes(2), CancellationToken.None);

        var events = await store.ListEventsAsync(alert.Id, CancellationToken.None);
        Assert.Equal([AlertEventKind.Opened, AlertEventKind.Escalated], events.Select(e => e.Kind));
        Assert.Equal(0.2, events[1].Value);
        var delivery = Assert.Single(await new DeliveryStore(database).ListForAlertAsync(alert.Id, default));
        Assert.Equal((warnings.Id, events[1].Id), (delivery.ChannelId, delivery.EventId));
    }

    [Fact]
    public async Task UpdateAsync_SeverityFalls_RecordsEasingWithoutDeliveries()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        await new ChannelStore(database).InsertAsync(Channels.Webhook("all"), CancellationToken.None);
        var alert = Alert("orders") with { Severity = AlertSeverity.Warning, Threshold = 0.05, Value = 0.2 };
        await store.InsertAsync(alert, CancellationToken.None);

        await store.UpdateAsync(
            alert.Id,
            AlertSeverity.Caution,
            0.04,
            0.01,
            _noon.AddMinutes(1),
            CancellationToken.None);


        var events = await store.ListEventsAsync(alert.Id, CancellationToken.None);
        Assert.Equal([AlertEventKind.Opened, AlertEventKind.Eased], events.Select(e => e.Kind));
        Assert.Single(await new DeliveryStore(database).ListForAlertAsync(alert.Id, default));
    }

    [Fact]
    public async Task UpdateAsync_RecordsWhenThePeakWasReached()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var alert = Alert("orders");
        await store.InsertAsync(alert, CancellationToken.None);

        await store.UpdateAsync(alert.Id, AlertSeverity.Warning, 0.3, 0.05, _noon.AddMinutes(1), CancellationToken.None);
        await store.UpdateAsync(alert.Id, AlertSeverity.Warning, 0.2, 0.05, _noon.AddMinutes(2), CancellationToken.None);

        Assert.Equal(_noon.AddMinutes(1), await store.GetPeakAtAsync(alert.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_QueuesResolvedOnlyForChannelsThatWereToldAndWantIt()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var channels = new ChannelStore(database);
        var told = await channels.InsertAsync(Channels.Webhook("told"), CancellationToken.None);
        await channels.InsertAsync(Channels.Webhook("told, no resolved") with { SendResolved = false }, default);
        var alert = Alert("orders");
        await store.InsertAsync(alert, CancellationToken.None);

        // Added after the alert opened, so it never heard of it.
        await channels.InsertAsync(Channels.Webhook("new"), CancellationToken.None);
        await store.ResolveAsync(alert.Id, 0.001, _noon.AddMinutes(9), CancellationToken.None);

        var events = await store.ListEventsAsync(alert.Id, CancellationToken.None);
        Assert.Equal(AlertEventKind.Resolved, events[^1].Kind);
        Assert.Equal(AlertSeverity.Caution, events[^1].Severity);
        var resolved = (await new DeliveryStore(database).ListForAlertAsync(alert.Id, default))
            .Single(delivery => delivery.EventId == events[^1].Id);
        Assert.Equal(told.Id, resolved.ChannelId);
        Assert.Equal(_noon.AddMinutes(9), resolved.NextAttemptAt);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);

        Assert.Null(await new AlertStore(database).GetAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteResolvedBeforeAsync_RemovesTheirEventsAndDeliveries()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        await new ChannelStore(database).InsertAsync(Channels.Webhook("all"), CancellationToken.None);
        var old = Alert("old");
        await store.InsertAsync(old, CancellationToken.None);
        await store.ResolveAsync(old.Id, 0, _noon.AddDays(-40), CancellationToken.None);

        await store.DeleteResolvedBeforeAsync(_noon.AddDays(-30), CancellationToken.None);

        Assert.Empty(await store.ListEventsAsync(old.Id, CancellationToken.None));
        Assert.Empty(await new DeliveryStore(database).ListForAlertAsync(old.Id, CancellationToken.None));
    }

    private static AlertState Alert(string service) => new(
        Id: Guid.CreateVersion7(),
        Rule: "error-rate",
        Environment: "production",
        Subject: AlertSubject.Service(service),
        Severity: AlertSeverity.Caution,
        Value: 0.02,
        Peak: 0.02,
        Threshold: 0.01,
        Window: TimeSpan.FromMinutes(5),
        OpenedAt: _noon,
        UpdatedAt: _noon,
        ResolvedAt: null);
}
