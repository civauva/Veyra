using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Incidents;
using Veyra.Alerting.Notifications;
using Veyra.Alerting.Tests.Notifications;
using Veyra.Core.Alerts;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.Incidents;

[Collection(PostgresGroup.Name)]
public class IncidentStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InsertAsync_FirstAlert_OpensAnIncidentTitledAsNotificationsWordIt()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        var alert = ErrorRate("orders", _noon);

        await alerts.InsertAsync(alert, CancellationToken.None);

        var incident = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);
        Assert.Equal(("orders logs 2% errors", AlertSeverity.Caution, IncidentStatus.Open), (incident.Title, incident.Severity, incident.Status));
        Assert.Equal(("production", _noon, null), (incident.Environment, incident.OpenedAt, incident.ResolvedAt));
        Assert.Equal($"INC-{incident.Number}", incident.Key);
        var joined = Assert.Single(await alerts.ListForIncidentsAsync([incident.Id], default));
        Assert.Equal(alert.Id, joined.Entry.Alert.Id);
    }

    [Fact]
    public async Task InsertAsync_ContainerAlertSoonAfterItsHostsAlert_JoinsTheHostsIncident()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);

        await alerts.InsertAsync(Disk("web-01", _noon), CancellationToken.None);
        await alerts.InsertAsync(Container("shop-api", "web-01", _noon.AddMinutes(10)), CancellationToken.None);

        var incident = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);
        Assert.Equal(2, (await alerts.ListForIncidentsAsync([incident.Id], default)).Count);
    }

    [Fact]
    public async Task InsertAsync_RelatedAlertAfterTheGroupingWindow_OpensAnotherIncident()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);

        await alerts.InsertAsync(Disk("web-01", _noon), CancellationToken.None);
        await alerts.InsertAsync(Container("shop-api", "web-01", _noon.AddMinutes(16)), CancellationToken.None);

        Assert.Equal(2, (await incidents.ListPageAsync(Everything(), default)).Items.Count);
    }

    [Fact]
    public async Task InsertAsync_UnrelatedOrOtherEnvironment_OpensAnotherIncident()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);

        await alerts.InsertAsync(ErrorRate("orders", _noon), CancellationToken.None);
        await alerts.InsertAsync(ErrorRate("billing", _noon.AddMinutes(1)), CancellationToken.None);
        await alerts.InsertAsync(
            ErrorRate("orders", _noon.AddMinutes(2)) with { Environment = "staging" },
            CancellationToken.None);

        Assert.Equal(3, (await incidents.ListPageAsync(Everything(), default)).Items.Count);
    }

    [Fact]
    public async Task InsertAsync_ServiceOnAHostWithAnAlert_JoinsThroughTheSeriesRelations()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var relations = new FakeRelations([new BlockLink(AlertBlockKind.Service, "orders", "production", "web-01")]);
        var (alerts, incidents) = Stores(database, relations);

        await alerts.InsertAsync(Disk("web-01", _noon), CancellationToken.None);
        await alerts.InsertAsync(ErrorRate("orders", _noon.AddMinutes(3)), CancellationToken.None);

        Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);
    }

    [Fact]
    public async Task InsertAsync_RelationsCannotBeRead_StillOpensTheAlertInAnIncident()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database, new FakeRelations(null));

        await alerts.InsertAsync(ErrorRate("orders", _noon), CancellationToken.None);

        Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);
    }

    [Fact]
    public async Task InsertAsync_WorseAlertJoins_TakesTheTitleAndSeverity()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);

        await alerts.InsertAsync(Disk("web-01", _noon), CancellationToken.None);
        await alerts.InsertAsync(
            Container("shop-api", "web-01", _noon.AddMinutes(1)) with { Severity = AlertSeverity.Warning },
            CancellationToken.None);

        var incident = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);
        Assert.Equal(AlertSeverity.Warning, incident.Severity);
        Assert.Equal("Container shop-api on web-01 uses 95% of its memory limit", incident.Title);
    }

    [Fact]
    public async Task UpdateAsync_AlertEscalates_RaisesTheIncidentsSeverity()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        var alert = ErrorRate("orders", _noon);
        await alerts.InsertAsync(alert, CancellationToken.None);

        await alerts.UpdateAsync(alert.Id, AlertSeverity.Warning, 0.2, 0.05, _noon.AddMinutes(1), default);

        Assert.Equal(AlertSeverity.Warning, Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items).Severity);
    }

    [Fact]
    public async Task ResolveAsync_LastOpenAlert_ResolvesTheIncident()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        var disk = Disk("web-01", _noon);
        var container = Container("shop-api", "web-01", _noon.AddMinutes(1));
        await alerts.InsertAsync(disk, CancellationToken.None);
        await alerts.InsertAsync(container, CancellationToken.None);

        await alerts.ResolveAsync(disk.Id, 0.5, _noon.AddMinutes(5), default);
        var halfway = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);
        await alerts.ResolveAsync(container.Id, 0.5, _noon.AddMinutes(9), default);
        var done = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);

        Assert.Equal((IncidentStatus.Open, null), (halfway.Status, halfway.ResolvedAt));
        Assert.Equal((IncidentStatus.Resolved, _noon.AddMinutes(9)), (done.Status, done.ResolvedAt));
    }

    [Fact]
    public async Task InsertAsync_RelatedAlertAfterTheIncidentResolved_OpensAnotherIncident()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        var disk = Disk("web-01", _noon);
        await alerts.InsertAsync(disk, CancellationToken.None);
        await alerts.ResolveAsync(disk.Id, 0.5, _noon.AddMinutes(2), default);

        await alerts.InsertAsync(Container("shop-api", "web-01", _noon.AddMinutes(3)), CancellationToken.None);

        Assert.Equal(2, (await incidents.ListPageAsync(Everything(), default)).Items.Count);
    }

    [Fact]
    public async Task DeleteResolvedBeforeAsync_AllAlertsDeleted_DeletesTheIncident()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        var old = ErrorRate("orders", _noon.AddDays(-40));
        var open = ErrorRate("billing", _noon);
        await alerts.InsertAsync(old, CancellationToken.None);
        await alerts.InsertAsync(open, CancellationToken.None);
        await alerts.ResolveAsync(old.Id, 0, _noon.AddDays(-39), default);

        await alerts.DeleteResolvedBeforeAsync(_noon.AddDays(-30), default);

        var left = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);
        Assert.Equal("billing logs 2% errors", left.Title);
    }

    [Fact]
    public async Task AcknowledgeAsync_OpenIncident_RecordsWhoAndWhenOnItAndItsTimeline()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        await alerts.InsertAsync(ErrorRate("orders", _noon), CancellationToken.None);
        var id = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items).Id;
        var user = Guid.NewGuid();

        var acknowledged = await incidents.AcknowledgeAsync(id, user, "Ana", _noon.AddMinutes(2), default);
        var again = await incidents.AcknowledgeAsync(id, Guid.NewGuid(), "Bo", _noon.AddMinutes(3), default);

        Assert.Equal(IncidentStatus.Acknowledged, acknowledged!.Status);
        Assert.Equal((_noon.AddMinutes(2), user, "Ana"), (acknowledged.AcknowledgedAt, acknowledged.AcknowledgedById, acknowledged.AcknowledgedByName));
        Assert.Equal(acknowledged, again);
        var moment = Assert.Single(await incidents.ListEventsAsync(id, default));
        Assert.Equal(new IncidentEvent(IncidentEventKind.Acknowledged, "Ana", _noon.AddMinutes(2)), moment);
    }

    [Fact]
    public async Task UnacknowledgeAsync_AcknowledgedIncident_OpensItAgainAndRecordsIt()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        await alerts.InsertAsync(ErrorRate("orders", _noon), CancellationToken.None);
        var id = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items).Id;
        await incidents.AcknowledgeAsync(id, null, "Ana", _noon.AddMinutes(2), default);

        var open = await incidents.UnacknowledgeAsync(id, null, "Bo", _noon.AddMinutes(4), default);

        Assert.Equal((IncidentStatus.Open, null, null), (open!.Status, open.AcknowledgedAt, open.AcknowledgedByName));
        Assert.Equal(
            [IncidentEventKind.Acknowledged, IncidentEventKind.Unacknowledged],
            (await incidents.ListEventsAsync(id, default)).Select(moment => moment.Kind));
    }

    [Fact]
    public async Task AcknowledgeAsync_ResolvedIncident_IsAConflict()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        var alert = ErrorRate("orders", _noon);
        await alerts.InsertAsync(alert, CancellationToken.None);
        await alerts.ResolveAsync(alert.Id, 0, _noon.AddMinutes(5), default);
        var id = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items).Id;

        await Assert.ThrowsAsync<AlertingConflictException>(
            () => incidents.AcknowledgeAsync(id, null, "Ana", _noon.AddMinutes(6), default));
        await Assert.ThrowsAsync<AlertingConflictException>(
            () => incidents.UnacknowledgeAsync(id, null, "Ana", _noon.AddMinutes(6), default));
    }

    [Fact]
    public async Task AcknowledgeAsync_UnknownIncident_ReturnsNull()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (_, incidents) = Stores(database);

        Assert.Null(await incidents.AcknowledgeAsync(Guid.NewGuid(), null, "Ana", _noon, default));
        Assert.Null(await incidents.UnacknowledgeAsync(Guid.NewGuid(), null, "Ana", _noon, default));
    }

    [Fact]
    public async Task ListPageAsync_OpenAndAcknowledgedFirstThenNewest_PageByPage()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        var resolvedNewest = ErrorRate("a", _noon.AddHours(-1));
        await alerts.InsertAsync(ErrorRate("b", _noon.AddHours(-5)), CancellationToken.None);
        await alerts.InsertAsync(ErrorRate("c", _noon.AddHours(-3)), CancellationToken.None);
        await alerts.InsertAsync(resolvedNewest, CancellationToken.None);
        await alerts.ResolveAsync(resolvedNewest.Id, 0, _noon.AddMinutes(-30), default);

        var first = await incidents.ListPageAsync(Everything() with { Limit = 2 }, default);
        var second = await incidents.ListPageAsync(Everything() with { Limit = 2, After = first.Next }, default);
        var resolved = await incidents.ListPageAsync(Everything() with { Status = IncidentStatus.Resolved }, default);

        Assert.Equal(["c logs 2% errors", "b logs 2% errors"], first.Items.Select(item => item.Title));
        Assert.Equal(["a logs 2% errors"], second.Items.Select(item => item.Title));
        Assert.Null(second.Next);
        Assert.Equal(["a logs 2% errors"], resolved.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task ListPageAsync_ResolvedBeforeThePeriod_IsLeftOut()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        var old = ErrorRate("old", _noon.AddDays(-10));
        await alerts.InsertAsync(old, CancellationToken.None);
        await alerts.ResolveAsync(old.Id, 0, _noon.AddDays(-9), default);

        var page = await incidents.ListPageAsync(Everything() with { From = _noon.AddDays(-7) }, default);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetAsync_AsIncidentReader_ReturnsTheIncidentWithItsAlerts()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        var disk = Disk("web-01", _noon);
        var container = Container("shop-api", "web-01", _noon.AddMinutes(1));
        await alerts.InsertAsync(disk, CancellationToken.None);
        await alerts.InsertAsync(container, CancellationToken.None);
        var id = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items).Id;

        var read = await ((IIncidentReader)incidents).GetAsync(id, default);

        Assert.Equal((id, "production", _noon, null), (read!.Id, read.Environment, read.OpenedAt, read.ResolvedAt));
        Assert.Equal([disk.Id, container.Id], read.Alerts.Select(alert => alert.Id));
        Assert.Null(await ((IIncidentReader)incidents).GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task ListPageAsync_OfAlerts_TellsTheIncidentOfEach()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        await alerts.InsertAsync(ErrorRate("orders", _noon), CancellationToken.None);
        var incident = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);

        var page = await alerts.ListPageAsync(new AlertListQuery(_noon.AddDays(-1), _noon.AddDays(1), 10), default);

        var entry = Assert.Single(page.Items);
        Assert.Equal((incident.Id, incident.Number), (entry.IncidentId, entry.IncidentNumber));
        Assert.Equal((incident.Id, incident.Number), await alerts.GetIncidentOfAsync(entry.Alert.Id, default));
    }

    [Fact]
    public async Task ClaimDueAsync_AlertOfAnIncident_CarriesTheIncident()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        await new ChannelStore(database).InsertAsync(Channels.Webhook("ops"), CancellationToken.None);
        await alerts.InsertAsync(ErrorRate("orders", _noon), CancellationToken.None);
        var incident = Assert.Single((await incidents.ListPageAsync(Everything(), default)).Items);

        var claimed = Assert.Single(await new DeliveryStore(database).ClaimDueAsync(_noon, 10, _noon.AddMinutes(5), default));

        Assert.Equal((incident.Id, incident.Number), (claimed.IncidentId, claimed.IncidentNumber));
    }

    [Fact]
    public async Task ClearAsync_DeletesTheIncidentsWithTheAlerts()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var (alerts, incidents) = Stores(database);
        await alerts.InsertAsync(ErrorRate("orders", _noon), CancellationToken.None);

        await new AlertClearer(database).ClearAsync(default);

        Assert.Empty((await incidents.ListPageAsync(Everything(), default)).Items);
    }

    private static (AlertStore Alerts, IncidentStore Incidents) Stores(
        NpgsqlDataSource database,
        IBlockRelations? relations = null)
    {
        var grouping = new IncidentGrouping(relations, null, NullLogger<IncidentGrouping>.Instance);
        var alerts = new AlertStore(database, grouping);

        return (alerts, new IncidentStore(database, alerts));
    }

    private static IncidentListQuery Everything() => new(_noon.AddDays(-90), _noon.AddDays(1), 50);

    private static AlertState ErrorRate(string service, DateTimeOffset openedAt) => new(
        Guid.CreateVersion7(openedAt),
        AlertRules.ErrorRate,
        "production",
        AlertSubject.Service(service),
        AlertSeverity.Caution,
        0.02,
        0.02,
        0.01,
        TimeSpan.FromMinutes(5),
        openedAt,
        openedAt,
        null);

    private static AlertState Disk(string host, DateTimeOffset openedAt) => ErrorRate(host, openedAt) with
    {
        Rule = AlertRules.DiskSpace,
        Subject = AlertSubject.Host(host, "/"),
        Value = 0.92,
        Peak = 0.92,
        Threshold = 0.9,
    };

    private static AlertState Container(string name, string host, DateTimeOffset openedAt) =>
        ErrorRate(name, openedAt) with
        {
            Rule = AlertRules.ContainerMemory,
            Subject = AlertSubject.Container(name, host),
            Value = 0.95,
            Peak = 0.95,
            Threshold = 0.9,
        };

    // Links that the Query module would read from series; null throws, as a failed read does.
    private sealed class FakeRelations(IReadOnlyList<BlockLink>? links) : IBlockRelations
    {
        public Task<IReadOnlyList<BlockLink>> ListLinksAsync(CancellationToken cancellationToken) =>
            links is null
                ? throw new InvalidOperationException("The metrics cannot be read.")
                : Task.FromResult(links);
    }
}
