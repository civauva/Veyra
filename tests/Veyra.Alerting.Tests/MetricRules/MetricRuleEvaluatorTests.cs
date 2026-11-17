using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Telemetry;
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
        options.DatabaseBlocking.Enabled = false;
        options.DatabaseDeadlocks.Enabled = false;
        options.DatabaseLongQuery.Enabled = false;
        options.DatabaseBackupAge.Enabled = false;
        options.DatabaseAgentJob.Enabled = false;
        options.ContainerRestarts.Enabled = false;
        options.ContainerMemory.Enabled = false;
        options.DatabaseConnections.Enabled = false;
        options.DatabaseWraparound.Enabled = false;

        await Evaluator(database, options).EvaluateAsync(CancellationToken.None);

        Assert.Equal(0, _health.ListCalls);
        Assert.Empty(await new AlertStore(database).ListAsync(DateTimeOffset.MinValue, CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_RestartingContainerNearItsLimit_OpensAnAlertPerRuleNamingItsHost()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _health.Containers =
        [
            new ContainerHealth("shop-api", "web-01", "production", 3, 0.93, 930, 1000),
            new ContainerHealth("worker", "web-01", "production", 0, 0.20, 200, 1000),
        ];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        var restarts = Assert.Single(await OpenAsync(database, AlertRules.ContainerRestarts));
        Assert.Equal(
            (AlertSubject.Container("shop-api", "web-01"), AlertSeverity.Caution, 3d, 2d, TimeSpan.FromMinutes(15)),
            (restarts.Subject, restarts.Severity, restarts.Value, restarts.Threshold, restarts.Window));
        var memory = Assert.Single(await OpenAsync(database, AlertRules.ContainerMemory));
        Assert.Equal(
            (AlertSubject.Container("shop-api", "web-01"), AlertSeverity.Caution, 0.93, 0.90),
            (memory.Subject, memory.Severity, memory.Value, memory.Threshold));
        Assert.Equal("production", memory.Environment);
    }

    [Fact]
    public async Task EvaluateAsync_ContainerQuietForFifteenMinutes_ResolvesItsRestartAlert()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var evaluator = Evaluator(database);
        _health.Containers = [Restarting(5)];
        await evaluator.EvaluateAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(10));
        _health.Containers = [Restarting(1)];
        await evaluator.EvaluateAsync(CancellationToken.None);
        var held = Assert.Single(await OpenAsync(database, AlertRules.ContainerRestarts));
        Assert.Equal(AlertSeverity.Warning, held.Severity);

        _clock.Advance(TimeSpan.FromMinutes(15));
        _health.Containers = [Restarting(0)];
        await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database, AlertRules.ContainerRestarts));
        Assert.Empty(await OpenAsync(database, AlertRules.ContainerMemory));
    }

    private static ContainerHealth Restarting(double restarts) =>
        new("shop-api", "web-01", "production", restarts, null, null, null);

    private static DiskHealth Disk(string mountpoint, double ratio, double? days) =>
        new("web-01", "production", mountpoint, ratio * 100, 100, ratio, days);

    [Fact]
    public async Task EvaluateAsync_SqlServerTrouble_OpensAnAlertPerRuleOnTheServerOrItsPart()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _health.Databases =
        [
            SqlServer(
                blockedLeast: 2,
                deadlocks: 3,
                databases:
                [
                    new DatabasePartHealth("shop", 840, 840, 31 * 3600),
                    new DatabasePartHealth("reporting", 0, 0, 3600),
                ],
                jobs:
                [
                    new AgentJobHealth("Nightly full backup - shop", true),
                    new AgentJobHealth("Purge sessions", false),
                ]),
        ];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        const string server = "microsoft.sql_server sql-prod-01";
        var blocking = Assert.Single(await OpenAsync(database, AlertRules.DatabaseBlocking));
        Assert.Equal(
            (AlertSubject.Database(server), AlertSeverity.Caution, 2d),
            (blocking.Subject, blocking.Severity, blocking.Value));
        var deadlocks = Assert.Single(await OpenAsync(database, AlertRules.DatabaseDeadlocks));
        Assert.Equal(
            (AlertSubject.Database(server), 3d, TimeSpan.FromMinutes(15)),
            (deadlocks.Subject, deadlocks.Value, deadlocks.Window));
        var longQuery = Assert.Single(await OpenAsync(database, AlertRules.DatabaseLongQuery));
        Assert.Equal(
            (AlertSubject.Database(server, "shop"), 840d, 300d),
            (longQuery.Subject, longQuery.Value, longQuery.Threshold));
        var backup = Assert.Single(await OpenAsync(database, AlertRules.DatabaseBackupAge));
        Assert.Equal((AlertSubject.Database(server, "shop"), 31d * 3600), (backup.Subject, backup.Value));
        var job = Assert.Single(await OpenAsync(database, AlertRules.DatabaseAgentJob));
        Assert.Equal(AlertSubject.Database(server, "Nightly full backup - shop"), job.Subject);
        Assert.Equal("production", job.Environment);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoredDatabase_HasNoBackupAlert()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _health.Databases = [SqlServer(databases: [new DatabasePartHealth("scratch", 0, 0, 90 * 3600)])];
        var options = new AlertingOptions();
        options.DatabaseBackupAge.IgnoredDatabases = "Scratch";

        await Evaluator(database, options).EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database, AlertRules.DatabaseBackupAge));
    }

    [Fact]
    public async Task EvaluateAsync_JobSucceedsAgain_ResolvesItsAlert()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var evaluator = Evaluator(database);
        _health.Databases = [SqlServer(jobs: [new AgentJobHealth("Purge sessions", true)])];
        await evaluator.EvaluateAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(10));
        _health.Databases = [SqlServer(jobs: [new AgentJobHealth("Purge sessions", false)])];
        await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database, AlertRules.DatabaseAgentJob));
    }

    [Fact]
    public async Task EvaluateAsync_PostgresTrouble_OpensConnectionsOnTheServerAndWraparoundOnItsDatabase()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _health.Databases =
        [
            Postgres(
                connections: 0.86,
                databases:
                [
                    new DatabasePartHealth("shop", 420, 420, null) { WraparoundRatio = 0.52 },
                    new DatabasePartHealth("reporting", 0, 0, null) { WraparoundRatio = 0.10 },
                ]),
        ];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        const string server = "postgresql pg-main";
        var connections = Assert.Single(await OpenAsync(database, AlertRules.DatabaseConnections));
        Assert.Equal(
            (AlertSubject.Database(server), AlertSeverity.Caution, 0.86, 0.80, TimeSpan.FromMinutes(5)),
            (
                connections.Subject,
                connections.Severity,
                connections.Value,
                connections.Threshold,
                connections.Window));
        var wraparound = Assert.Single(await OpenAsync(database, AlertRules.DatabaseWraparound));
        Assert.Equal(
            (AlertSubject.Database(server, "shop"), AlertSeverity.Caution, 0.52, 0.50),
            (wraparound.Subject, wraparound.Severity, wraparound.Value, wraparound.Threshold));
        var longQuery = Assert.Single(await OpenAsync(database, AlertRules.DatabaseLongQuery));
        Assert.Equal(AlertSubject.Database(server, "shop"), longQuery.Subject);
        Assert.Empty(await OpenAsync(database, AlertRules.DatabaseBackupAge));

        _clock.Advance(TimeSpan.FromMinutes(5));
        _health.Databases =
        [
            Postgres(
                connections: 0.70,
                databases: [new DatabasePartHealth("shop", 0, 0, null) { WraparoundRatio = 0.03 }]),
        ];
        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database, AlertRules.DatabaseConnections));
        Assert.Empty(await OpenAsync(database, AlertRules.DatabaseWraparound));
    }

    [Fact]
    public async Task EvaluateAsync_SqlServerWithoutConnectionLimit_HasNoConnectionsOrWraparoundAlert()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        _health.Databases = [SqlServer(databases: [new DatabasePartHealth("shop", 0, 0, 3600)])];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database, AlertRules.DatabaseConnections));
        Assert.Empty(await OpenAsync(database, AlertRules.DatabaseWraparound));
    }

    private static DatabaseHealth Postgres(double connections, IReadOnlyList<DatabasePartHealth> databases) => new(
        TelemetryBlock.Database("postgresql", "pg-main"),
        "production",
        0,
        0,
        0,
        0,
        databases,
        [])
    {
        ConnectionsRatio = connections,
    };

    private static DatabaseHealth SqlServer(
        double blockedLeast = 0,
        double deadlocks = 0,
        IReadOnlyList<DatabasePartHealth>? databases = null,
        IReadOnlyList<AgentJobHealth>? jobs = null) => new(
            TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01"),
            "production",
            blockedLeast,
            blockedLeast,
            deadlocks,
            deadlocks,
            databases ?? [],
            jobs ?? []);

    [Fact]
    public async Task EvaluateAsync_CatalogMeasures_OpenEscalateAndResolveTheirAlerts()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var server = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");
        _health.Measures =
        [
            new DatabaseMeasure(server, "production", "head-blocker-seconds", null, 90),
            new DatabaseMeasure(server, "production", "open-transaction-seconds", null, 2_000),
            new DatabaseMeasure(server, "production", "tempdb-session-share", null, 0.1),
        ];

        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        var blocker = Assert.Single(await OpenAsync(database, "db-head-blocker"));
        Assert.Equal((AlertSeverity.Caution, 60d), (blocker.Severity, blocker.Threshold));
        Assert.Equal(AlertSubject.Database(server.Name), blocker.Subject);
        Assert.Equal(AlertSeverity.Warning, Assert.Single(await OpenAsync(database, "db-open-transaction")).Severity);
        Assert.Empty(await OpenAsync(database, "db-tempdb-session"));

        _health.Measures = [new DatabaseMeasure(server, "production", "head-blocker-seconds", null, 0)];
        _clock.Advance(TimeSpan.FromMinutes(1));
        await Evaluator(database).EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database, "db-head-blocker"));
    }

    [Fact]
    public async Task EvaluateAsync_CatalogRuleSwitchedOffOrLevelsChanged_FollowsTheSettings()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var server = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");
        _health.Measures =
        [
            new DatabaseMeasure(server, null, "head-blocker-seconds", null, 90),
            new DatabaseMeasure(server, null, "open-transaction-seconds", null, 400),
        ];
        var options = new AlertingOptions();
        options.Catalog["HeadBlocker"] = new() { Enabled = false };
        options.Catalog["OpenTransaction"] = new() { Caution = 5, Warning = 6 };

        await Evaluator(database, options).EvaluateAsync(CancellationToken.None);

        Assert.Empty(await OpenAsync(database, "db-head-blocker"));
        Assert.Equal(AlertSeverity.Warning, Assert.Single(await OpenAsync(database, "db-open-transaction")).Severity);
    }

    private MetricRuleEvaluator Evaluator(NpgsqlDataSource database, AlertingOptions? options = null) => new(
        _health,
        new AlertStore(database),
        new AlertTransitions(new AlertStore(database), NullLogger<AlertTransitions>.Instance),
        new TestOptionsMonitor<AlertingOptions>(options ?? new AlertingOptions()),
        _clock);

    private static Task<IReadOnlyList<AlertState>> OpenAsync(NpgsqlDataSource database, string rule) =>
        new AlertStore(database).ListOpenAsync(rule, CancellationToken.None);
}
