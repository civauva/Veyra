using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.ErrorRates;
using Veyra.Alerting.MetricRules;
using Veyra.Alerting.Tests.ErrorRates;
using Veyra.Alerting.Tests.MetricRules;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Alerting.Tests;

[Collection(PostgresGroup.Name)]
public class AlertRuleRunTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(_noon);
    private readonly FakeLogRates _rates = new();
    private readonly FakeMetricHealth _health = new();

    [Fact]
    public async Task RunAsync_KeepsResolvedAlertsForTheHistoryDays()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        var recent = await ResolvedAsync(store, "recent", _noon.AddDays(-60));
        await ResolvedAsync(store, "old", _noon.AddDays(-91));

        await Run(database).RunAsync(CancellationToken.None);

        var kept = await store.ListAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Equal([recent.Id], kept.Select(alert => alert.Id));
    }

    [Fact]
    public async Task RunAsync_ShorterHistory_DropsOlderAlerts()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new AlertStore(database);
        await ResolvedAsync(store, "recent", _noon.AddDays(-8));

        await Run(database, new AlertingOptions { HistoryDays = 7 }).RunAsync(CancellationToken.None);

        Assert.Empty(await store.ListAsync(DateTimeOffset.MinValue, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_EvaluatesTheErrorRateAndTheMetricRules()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _rates.Rates = [new ServiceLogRate("production", "payments", 100, 30)];
        _health.Disks = [new DiskHealth("web-01", "production", "/", 96, 100, 0.96, null)];

        await Run(database).RunAsync(CancellationToken.None);

        var open = await new AlertStore(database).ListAsync(_noon, CancellationToken.None);
        Assert.Equal([AlertRules.DiskSpace, AlertRules.ErrorRate], open.Select(alert => alert.Rule).Order());
    }

    private static async Task<AlertState> ResolvedAsync(AlertStore store, string service, DateTimeOffset resolvedAt)
    {
        var alert = new AlertState(
            Guid.CreateVersion7(),
            AlertRules.ErrorRate,
            "production",
            AlertSubject.Service(service),
            AlertSeverity.Caution,
            0.02,
            0.02,
            0.01,
            TimeSpan.FromMinutes(5),
            resolvedAt.AddHours(-1),
            resolvedAt.AddHours(-1),
            null);
        await store.InsertAsync(alert, CancellationToken.None);
        await store.ResolveAsync(alert.Id, 0, resolvedAt, CancellationToken.None);

        return alert;
    }

    private AlertRuleRun Run(NpgsqlDataSource database, AlertingOptions? options = null)
    {
        var settings = Options.Create(options ?? new AlertingOptions());
        var store = new AlertStore(database);
        var transitions = new AlertTransitions(store, NullLogger<AlertTransitions>.Instance);

        return new AlertRuleRun(
            store,
            new ErrorRateEvaluator(_rates, store, transitions, settings, _clock),
            new MetricRuleEvaluator(_health, store, transitions, settings, _clock),
            settings,
            _clock);
    }
}
