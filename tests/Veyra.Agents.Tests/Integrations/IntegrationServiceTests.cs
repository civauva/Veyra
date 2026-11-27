using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Agents.Agents;
using Veyra.Agents.Collector;
using Veyra.Agents.Enrollment;
using Veyra.Agents.Integrations;
using Veyra.Agents.PostgresServers;
using Veyra.Agents.Probes;
using Veyra.Agents.SqlServers;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Agents.Tests.Integrations;

[Collection(PostgresGroup.Name)]
public class IntegrationServiceTests(PostgresFixture postgres)
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));

    private readonly EphemeralDataProtectionProvider _protection = new();

    private readonly FakeMonitoringHistory _history = new();

    private readonly FakeLogSearch _logs = new();

    private readonly FakeMetricSearch _metrics = new();

    private readonly FakeSavedSettings _settings = new();

    private readonly FakeNotificationChannels _channels = new();

    [Fact]
    public async Task ListAsync_Server_IsAnIntegrationWithItsVersionAndModules()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var server = await AddServerAsync(database);

        var item = Assert.Single(await Service(database).ListAsync(default));

        Assert.Equal(
            (server.Id, "sqlserver", "sql-prod-01", "production", "Built in", "0.2.0", "0.2.0"),
            (item.Id, item.Kind, item.Name, item.Environment, item.Agent.Name, item.Version, item.LatestVersion));
        Assert.Equal(SqlServerModules.Defaults(), item.Modules);
        Assert.Equal(SqlServerState.Receiving, item.State);
    }

    [Fact]
    public async Task DetailAsync_Server_ShowsItsPackageCollectorAndHistory()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var server = await AddServerAsync(database);

        var detail = (await Service(database).DetailAsync(server.Id, Window(), default))!;

        var package = detail.Package;
        Assert.Equal(
            ("veyra-sqlserver-integration", "0.2.0", "0.2.0", (string?)"0.1.0", (string?)"Lena Adler"),
            (package.Name, package.Version, package.LatestVersion, package.PreviousVersion, package.SetBy));
        Assert.Equal(
            ((string?)"0.140.1", "otelcol-contrib 0.140", "SQL Server 2019", (string?)"veyra_monitor"),
            (package.CollectorVersion, package.CollectorCompatibility, package.MinimumSqlServer, package.Login));
        Assert.Equal(
            ((bool?)true, (double?)TimeSpan.FromHours(3).TotalSeconds, "applied"),
            (detail.Collector.Healthy, detail.Collector.UptimeSeconds, detail.Collector.ConfigState));
        Assert.Equal("added", Assert.Single(detail.History).Kind);
        Assert.Null(detail.Validation.LastProbe);
    }

    [Fact]
    public async Task DetailAsync_RolledBack_HasNoPreviousVersionAndAVersionChangeInItsHistory()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var server = await AddServerAsync(database);
        await Servers(database).SetVersionAsync(server.Id, "0.1.0", FakeMonitoringHistory.Admin, default);

        var detail = (await Service(database).DetailAsync(server.Id, Window(), default))!;

        Assert.Equal(("0.1.0", (string?)null), (detail.Package.Version, detail.Package.PreviousVersion));
        var change = detail.History[0];
        Assert.Equal(("version", (string?)"0.2.0", (string?)"0.1.0"), (change.Kind, change.From, change.To));
    }

    [Fact]
    public async Task DetailAsync_Metrics_AreCountedPerModuleWithALineOfPoints()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var server = await AddServerAsync(database);
        var window = Window();
        _metrics.Series.Add(Series(1, "sqlserver.file.size"));
        _metrics.Series.Add(Series(2, "sqlserver.batch.request.rate"));
        _metrics.Buckets.Add(Bucket(1, window.From, points: 4));
        _metrics.Buckets.Add(Bucket(1, window.From + (window.Bucket * 2), points: 6));
        _metrics.Buckets.Add(Bucket(2, window.From, points: 20));

        var modules = (await Service(database).DetailAsync(server.Id, window, default))!.Modules;

        var storage = modules.Single(module => module.Id == "storage");
        Assert.Equal((1, 10L), (storage.Series, storage.Points));
        Assert.Equal(60, storage.Line.Count);
        Assert.Equal([4, null, 6], storage.Line.Take(3));
        Assert.Equal((1, 20L), (modules[0].Series, modules[0].Points));
        var jobs = modules.Single(module => module.Id == "jobs");
        Assert.Equal((0, 0L), (jobs.Series, jobs.Points));
    }

    [Fact]
    public async Task DetailAsync_AgentStatements_GiveEachCheckItsTimePerRun()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var server = await AddServerAsync(database);
        var window = Window();
        var sessions = new Dictionary<string, string>
        {
            ["agent"] = "true",
            ["query_text"] = "/* veyra-agent: sessions */ SELECT 'sqlserver.session' AS veyra_record",
        };
        _metrics.Series.Add(Series(1, "sqlserver.statement.elapsed", sessions));
        _metrics.Series.Add(Series(2, "sqlserver.statement.executions", sessions));
        _metrics.Buckets.Add(Bucket(1, window.From, points: 1, total: 2.0));
        _metrics.Buckets.Add(Bucket(2, window.From, points: 1, total: 4));
        _clock.Advance(TimeSpan.FromMinutes(1));
        _logs.AddReaderError(
            $"sqlquery/plans/{CollectorConfig.ComponentSuffix(server.Id)}",
            "permission was denied",
            _clock.GetUtcNow().AddSeconds(-30));

        var checks = (await Service(database).DetailAsync(server.Id, window, default))!.Checks;

        var live = checks.Single(check => check.Name == "sessions");
        Assert.Equal(("activity", (double?)0.5, (long?)4, true), (live.Module, live.SecondsPerRun, live.Runs, live.Ok));
        var plans = checks.Single(check => check.Name == "plans");
        Assert.Equal(((double?)null, false), (plans.SecondsPerRun, plans.Ok));
    }

    [Fact]
    public async Task DetailAsync_PermissionReportedMissing_IsWhatTheLoginLacks()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var server = await AddServerAsync(database);
        var now = _clock.GetUtcNow();
        _metrics.Series.Add(
            Series(1, IntegrationService.MissingPermission, new() { ["permission"] = "VIEW SERVER STATE" }));
        _metrics.Series.Add(
            Series(2, IntegrationService.MissingPermission, new() { ["permission"] = "VIEW ANY DEFINITION" }));
        _metrics.Buckets.Add(Bucket(1, now.AddMinutes(-15), points: 1, last: 1, lastAt: now.AddMinutes(-3)));
        _metrics.Buckets.Add(Bucket(2, now.AddMinutes(-15), points: 1, last: 0, lastAt: now.AddMinutes(-3)));

        var package = (await Service(database).DetailAsync(server.Id, Window(), default))!.Package;

        Assert.Equal(["VIEW SERVER STATE"], package.LoginLacks);
    }

    [Fact]
    public async Task DetailAsync_NextSteps_SayWhatTheInstallationStillLacks()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);
        await SeedAgentAsync(database);
        var server = await AddServerAsync(database);
        _settings.Keys.Add("Veyra:Alerting:DatabaseBlocking:WarningSessions");
        _settings.Keys.Add("Veyra:Storage:MaxLogStorageGigabytes");

        var steps = (await Service(database).DetailAsync(server.Id, Window(), default))!.NextSteps;

        Assert.Equal(
            [("thresholds", true), ("channel", false), ("retention", false)],
            steps.Select(step => (step.Id, step.Done)));
        Assert.Equal("/notifications", steps[1].Link);
    }

    [Fact]
    public async Task DetailAsync_UnknownServer_IsNull()
    {
        await using var database = await AgentsDatabase.CreateAsync(postgres);

        Assert.Null(await Service(database).DetailAsync(Guid.CreateVersion7(), Window(), default));
    }

    [Fact]
    public void Window_SixHours_HasSixtyBucketsOfSixMinutes()
    {
        var window = IntegrationWindow.Resolve(null, null, null, _clock.GetUtcNow())!;

        Assert.Equal((TimeSpan.FromMinutes(6), 60), (window.Bucket, window.Points));
        Assert.Null(IntegrationWindow.Resolve(null, _clock.GetUtcNow(), _clock.GetUtcNow(), _clock.GetUtcNow()));
        Assert.Null(IntegrationWindow.Resolve(null, _clock.GetUtcNow().AddDays(-32), null, _clock.GetUtcNow()));
    }

    private IntegrationWindow Window() => IntegrationWindow.Resolve("6h", null, null, _clock.GetUtcNow())!;

    private MetricSeries Series(long id, string name, Dictionary<string, string>? attributes = null) =>
        new(
            id,
            name,
            MetricKind.Gauge,
            "1",
            "",
            MetricTemporality.Unspecified,
            false,
            null,
            "production",
            null,
            new Dictionary<string, string>(),
            attributes ?? [],
            _clock.GetUtcNow().AddDays(-1),
            _clock.GetUtcNow());

    private static MetricBucket Bucket(
        long series,
        DateTimeOffset start,
        int points,
        double? total = null,
        double? last = null,
        DateTimeOffset? lastAt = null) =>
        new(series, start, points, null, total, lastAt ?? start, null, last, null);

    private async Task SeedAgentAsync(NpgsqlDataSource database)
    {
        var agent = AgentsDatabase.BuiltIn() with
        {
            Report = AgentReport.None with
            {
                Version = "0.140.1",
                Healthy = true,
                ConfigState = AgentConfigState.Applied,
                LastSeenAt = _clock.GetUtcNow(),
                StartedAt = _clock.GetUtcNow().AddHours(-3),
            },
        };
        var store = new AgentStore(database);
        await store.InsertAsync(agent, default);
        await store.SaveReportAsync(agent.Id, agent.Report, default);
        _metrics.Series.Add(Series(99, "sqlserver.cpu.count"));
    }

    private async Task<SqlServer> AddServerAsync(NpgsqlDataSource database) =>
        await Servers(database).AddAsync(
            new SqlServerInput(
                "sql-prod-01",
                "10.0.0.5",
                1433,
                "production",
                null,
                "veyra_monitor",
                "Server-Horse-1",
                SqlServerEncryption.Required),
            FakeMonitoringHistory.Admin,
            default);

    private SqlServerService Servers(NpgsqlDataSource database) =>
        new(
            new SqlServerStore(database),
            new AgentStore(database),
            new SqlServerPasswordProtector(_protection),
            _clock,
            _history,
            NullLogger<SqlServerService>.Instance);

    private IntegrationService Service(NpgsqlDataSource database)
    {
        var signals = new SqlServerSignals(_logs, _metrics);
        var postgresServers = new PostgresServerService(
            new PostgresServerStore(database),
            new AgentStore(database),
            new PostgresPasswordProtector(_protection),
            _clock,
            _history,
            NullLogger<PostgresServerService>.Instance);
        var overview = new AgentsOverview(
            new AgentStore(database),
            Servers(database),
            postgresServers,
            signals,
            new EnrollmentStore(database),
            _clock);
        var probes = new ProbeService(
            new ProbeStore(database),
            Servers(database),
            new AgentStore(database),
            new SqlServerPasswordProtector(_protection),
            signals,
            _clock,
            NullLogger<ProbeService>.Instance);

        return new IntegrationService(
            overview,
            new AgentStore(database),
            probes,
            signals,
            _metrics,
            _history,
            _settings,
            _channels,
            _clock);
    }
}
