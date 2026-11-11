using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.MetricRules;

[Collection(PostgresGroup.Name)]
public class MetricRuleEvaluatorTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(_noon);
    private readonly FakeMetricHealth _health = new();

    [Fact]
    public async Task EvaluateAsync_FullDisk_OpensDiskSpaceAlertForTheHostAndMountPoint()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _health.Disks = [Disk("/var/log", ratio: 0.91, days: null)];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        var alert = Assert.Single(await OpenAsync(database, AlertRules.DiskSpace));
        Assert.Equal(AlertSubject.Host("web-01", "/var/log"), alert.Subject);
        Assert.Equal("production", alert.Environment);
        Assert.Equal(AlertSeverity.Caution, alert.Severity);
        Assert.Equal(0.91, alert.Value);
        Assert.Equal(0.90, alert.Threshold);
        Assert.Equal(TimeSpan.FromMinutes(5), alert.Window);
        Assert.Empty(await OpenAsync(database, AlertRules.DiskFilling));
    }

    [Fact]
    public async Task EvaluateAsync_FillingDisk_OpensDiskFillingAlertWithTheDaysUntilFull()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _health.Disks = [Disk("/", ratio: 0.86, days: 9.2)];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        var alert = Assert.Single(await OpenAsync(database, AlertRules.DiskFilling));
        Assert.Equal(AlertSubject.Host("web-01", "/"), alert.Subject);
        Assert.Equal(AlertSeverity.Caution, alert.Severity);
        Assert.Equal(9.2, alert.Value);
        Assert.Equal(14, alert.Threshold);
        Assert.Equal(TimeSpan.FromDays(1), alert.Window);
        Assert.Empty(await OpenAsync(database, AlertRules.DiskSpace));
    }

    [Fact]
    public async Task EvaluateAsync_DiskStopsFilling_ResolvesAsNotFilling()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var evaluator = Evaluator(database);
        _health.Disks = [Disk("/", ratio: 0.86, days: 9.2)];
        await evaluator.EvaluateAsync(CancellationToken.None);
        var opened = Assert.Single(await OpenAsync(database, AlertRules.DiskFilling));

        _clock.Advance(TimeSpan.FromHours(1));
        _health.Disks = [Disk("/", ratio: 0.86, days: null)];
        await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database, AlertRules.DiskFilling));
        var resolved = (await new AlertStore(database).GetAsync(opened.Id, CancellationToken.None))!;
        Assert.Equal(DiskFillingRule.NotFilling, resolved.Value);
        Assert.Equal(9.2, resolved.Peak);
        Assert.Equal(_noon.AddHours(1), resolved.ResolvedAt);
    }

    [Fact]
    public async Task EvaluateAsync_DiskNoLongerReported_KeepsItsAlert()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var evaluator = Evaluator(database);
        _health.Disks = [Disk("/", ratio: 0.97, days: 1)];
        await evaluator.EvaluateAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(5));
        _health.Disks = [];
        await evaluator.EvaluateAsync(CancellationToken.None);

        var space = Assert.Single(await OpenAsync(database, AlertRules.DiskSpace));
        var filling = Assert.Single(await OpenAsync(database, AlertRules.DiskFilling));
        Assert.Equal(AlertSeverity.Warning, space.Severity);
        Assert.Equal(_noon, space.UpdatedAt);
        Assert.Equal(AlertSeverity.Warning, filling.Severity);
    }

    [Fact]
    public async Task EvaluateAsync_SlowService_OpensLatencyWarning()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _health.Latencies = [new ServiceLatency("checkout-web", "production", 3.4, 400)];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        var alert = Assert.Single(await OpenAsync(database, AlertRules.LatencyP95));
        Assert.Equal(AlertSubject.Service("checkout-web"), alert.Subject);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Equal(3.4, alert.Value);
        Assert.Equal(3, alert.Threshold);
    }

    [Fact]
    public async Task EvaluateAsync_TooFewRequests_OpensNothingAndKeepsAnOpenAlertsValue()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var evaluator = Evaluator(database);
        _health.Latencies = [new ServiceLatency("quiet", "production", 9, 5)];
        await evaluator.EvaluateAsync(CancellationToken.None);
        Assert.Empty(await OpenAsync(database, AlertRules.LatencyP95));

        _health.Latencies = [new ServiceLatency("quiet", "production", 1.5, 100)];
        await evaluator.EvaluateAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(30));
        _health.Latencies = [new ServiceLatency("quiet", "production", 0.1, 3)];
        await evaluator.EvaluateAsync(CancellationToken.None);

        var alert = Assert.Single(await OpenAsync(database, AlertRules.LatencyP95));
        Assert.Equal(1.5, alert.Value);
        Assert.Equal(AlertSeverity.Caution, alert.Severity);
    }

    [Fact]
    public async Task EvaluateAsync_RulesSwitchedOff_ReadNothingAndOpenNothing()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _health.Disks = [Disk("/", ratio: 0.99, days: 0.5)];
        _health.Latencies = [new ServiceLatency("checkout-web", "production", 9, 400)];
        var options = new AlertingOptions();
        options.DiskSpace.Enabled = false;
        options.DiskFilling.Enabled = false;
        options.Latency.Enabled = false;

        await Evaluator(database, options).EvaluateAsync(CancellationToken.None);

        Assert.Equal(0, _health.ListCalls);
        Assert.Empty(await new AlertStore(database).ListAsync(DateTimeOffset.MinValue, CancellationToken.None));
    }

    private static DiskHealth Disk(string mountpoint, double ratio, double? days) =>
        new("web-01", "production", mountpoint, ratio * 100, 100, ratio, days);

    private MetricRuleEvaluator Evaluator(NpgsqlDataSource database, AlertingOptions? options = null) => new(
        _health,
        new AlertStore(database),
        new AlertTransitions(new AlertStore(database), NullLogger<AlertTransitions>.Instance),
        Options.Create(options ?? new AlertingOptions()),
        _clock);

    private static Task<IReadOnlyList<AlertState>> OpenAsync(NpgsqlDataSource database, string rule) =>
        new AlertStore(database).ListOpenAsync(rule, CancellationToken.None);
}
