using Veyra.Alerting.Alerts;
using Veyra.Core.Alerts;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.Alerts;

/// <summary>The store's side of ADR 0012: alerts about hosts and their parts, and the history of the Alerts screen.</summary>
[Collection(PostgresGroup.Name)]
public class AlertHistoryStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InsertAsync_HostAlertWithAPart_RoundTripsTheSubject()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var alert = Disk("web-01", "/var/log");

        await store.InsertAsync(alert, CancellationToken.None);

        Assert.Equal(alert, await store.GetAsync(alert.Id, CancellationToken.None));
    }

    [Fact]
    public async Task InsertAsync_TwoDisksOfOneHost_AreTwoOpenAlerts()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);

        await store.InsertAsync(Disk("web-01", "/"), CancellationToken.None);
        await store.InsertAsync(Disk("web-01", "/var/log"), CancellationToken.None);

        Assert.Equal(2, (await store.ListOpenAsync(AlertRules.DiskFilling, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task InsertAsync_SameDiskTwice_Throws()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        await store.InsertAsync(Disk("web-01", "/"), CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() => store.InsertAsync(Disk("web-01", "/"), CancellationToken.None));
    }

    [Fact]
    public async Task InsertAsync_HostAndServiceOfTheSameName_AreDifferentSubjects()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);

        await store.InsertAsync(Service("web-01"), CancellationToken.None);
        await store.InsertAsync(Service("web-01") with { Subject = AlertSubject.Host("web-01") }, CancellationToken.None);

        Assert.Equal(2, (await store.ListOpenAsync(AlertRules.ErrorRate, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task UpdateAsync_DiskFilling_KeepsTheFewestDaysAsThePeak()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var alert = Disk("web-01", "/");
        await store.InsertAsync(alert, CancellationToken.None);

        await store.UpdateAsync(alert.Id, AlertSeverity.Warning, 2.5, 3, _noon.AddHours(1), CancellationToken.None);
        await store.UpdateAsync(alert.Id, AlertSeverity.Caution, 6, 14, _noon.AddHours(2), CancellationToken.None);

        var stored = (await store.GetAsync(alert.Id, CancellationToken.None))!;
        Assert.Equal(6, stored.Value);
        Assert.Equal(2.5, stored.Peak);
        Assert.Equal(_noon.AddHours(1), await store.GetPeakAtAsync(alert.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListPageAsync_RemembersTheWorstSeverityAfterItEased()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var alert = Service("orders");
        await store.InsertAsync(alert, CancellationToken.None);
        await store.UpdateAsync(alert.Id, AlertSeverity.Warning, 0.2, 0.05, _noon.AddMinutes(1), CancellationToken.None);
        await store.UpdateAsync(alert.Id, AlertSeverity.Caution, 0.03, 0.01, _noon.AddMinutes(2), CancellationToken.None);
        await store.ResolveAsync(alert.Id, 0.001, _noon.AddMinutes(3), CancellationToken.None);

        var page = await store.ListPageAsync(Query(), CancellationToken.None);

        var entry = Assert.Single(page.Items);
        Assert.Equal(AlertSeverity.Caution, entry.Alert.Severity);
        Assert.Equal(AlertSeverity.Warning, entry.Worst);
        Assert.Equal(_noon.AddMinutes(1), entry.PeakAt);
    }

    [Fact]
    public async Task ListPageAsync_PagesNewestFirstWithoutRepeats()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);

        for (var minute = 0; minute < 5; minute++)
        {
            await store.InsertAsync(Service($"service-{minute}") with { OpenedAt = _noon.AddMinutes(minute) }, CancellationToken.None);
        }

        var first = await store.ListPageAsync(Query() with { Limit = 3 }, CancellationToken.None);
        var second = await store.ListPageAsync(Query() with { Limit = 3, After = first.Next }, CancellationToken.None);

        Assert.Equal(["service-4", "service-3", "service-2"], first.Items.Select(entry => entry.Alert.Subject.Name));
        Assert.Equal(["service-1", "service-0"], second.Items.Select(entry => entry.Alert.Subject.Name));
        Assert.NotNull(first.Next);
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task ListPageAsync_Filters_NarrowTheList()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var resolved = Service("orders-api");
        await store.InsertAsync(resolved, CancellationToken.None);
        await store.ResolveAsync(resolved.Id, 0, _noon.AddMinutes(10), CancellationToken.None);
        await store.InsertAsync(Service("payments") with { Severity = AlertSeverity.Warning }, CancellationToken.None);
        await store.InsertAsync(Disk("web-01", "/var/log"), CancellationToken.None);
        await store.InsertAsync(Disk("db-01", "/") with { Environment = "staging" }, CancellationToken.None);

        async Task<string[]> NamesAsync(AlertListQuery query) =>
            (await store.ListPageAsync(query, CancellationToken.None)).Items
                .Select(entry => entry.Alert.Subject.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(["orders-api"], await NamesAsync(Query() with { Open = false }));
        Assert.Equal(["db-01", "payments", "web-01"], await NamesAsync(Query() with { Open = true }));
        Assert.Equal(["db-01", "web-01"], await NamesAsync(Query() with { Rules = [AlertRules.DiskFilling] }));
        Assert.Equal(["payments"], await NamesAsync(Query() with { Severity = AlertSeverity.Warning }));
        Assert.Equal(["db-01"], await NamesAsync(Query() with { Environment = "staging" }));
        Assert.Equal(["db-01", "web-01"], await NamesAsync(Query() with { Kind = AlertBlockKind.Host }));
        Assert.Equal(["web-01"], await NamesAsync(Query() with { Block = "VAR/" }));
        Assert.Equal(["orders-api"], await NamesAsync(Query() with { Block = "s-a" }));
        Assert.Empty(await NamesAsync(Query() with { Block = "%" }));
    }

    [Fact]
    public async Task ListPageAsync_Period_LeavesOutAlertsResolvedBeforeItAndOpenedAfterIt()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var before = Service("before") with { OpenedAt = _noon.AddDays(-40) };
        await store.InsertAsync(before, CancellationToken.None);
        await store.ResolveAsync(before.Id, 0, _noon.AddDays(-39), CancellationToken.None);
        var still = Service("still-open") with { OpenedAt = _noon.AddDays(-40) };
        await store.InsertAsync(still, CancellationToken.None);
        await store.InsertAsync(Service("later") with { OpenedAt = _noon.AddDays(1) }, CancellationToken.None);

        var page = await store.ListPageAsync(
            Query() with { From = _noon.AddDays(-30), To = _noon },
            CancellationToken.None);

        Assert.Equal(["still-open"], page.Items.Select(entry => entry.Alert.Subject.Name));
    }

    [Fact]
    public async Task ListSpansAsync_ReturnsRuleSeveritiesAndTimes()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var alert = Disk("web-01", "/");
        await store.InsertAsync(alert, CancellationToken.None);
        await store.UpdateAsync(alert.Id, AlertSeverity.Warning, 2, 3, _noon.AddHours(1), CancellationToken.None);
        await store.ResolveAsync(alert.Id, 365, _noon.AddHours(2), CancellationToken.None);
        await store.InsertAsync(Service("orders") with { Environment = "staging" }, CancellationToken.None);

        var spans = await store.ListSpansAsync(_noon.AddDays(-1), _noon.AddDays(1), "production", CancellationToken.None);

        var span = Assert.Single(spans);
        Assert.Equal(
            new AlertSpan(AlertRules.DiskFilling, AlertSeverity.Warning, AlertSeverity.Warning, _noon, _noon.AddHours(2)),
            span);
    }

    private static AlertListQuery Query() => new(_noon.AddDays(-30), _noon.AddDays(1), 50);

    private static AlertState Service(string name) => new(
        Id: Guid.CreateVersion7(),
        Rule: AlertRules.ErrorRate,
        Environment: "production",
        Subject: AlertSubject.Service(name),
        Severity: AlertSeverity.Caution,
        Value: 0.02,
        Peak: 0.02,
        Threshold: 0.01,
        Window: TimeSpan.FromMinutes(5),
        OpenedAt: _noon,
        UpdatedAt: _noon,
        ResolvedAt: null);

    private static AlertState Disk(string host, string mountpoint) => new(
        Id: Guid.CreateVersion7(),
        Rule: AlertRules.DiskFilling,
        Environment: "production",
        Subject: AlertSubject.Host(host, mountpoint),
        Severity: AlertSeverity.Caution,
        Value: 9,
        Peak: 9,
        Threshold: 14,
        Window: TimeSpan.FromDays(1),
        OpenedAt: _noon,
        UpdatedAt: _noon,
        ResolvedAt: null);
}
