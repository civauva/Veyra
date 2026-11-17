using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;

namespace Veyra.Query.Tests.Metrics;

public class DatabaseHealthTests
{
    // 12:30:20 UTC: the last five one-minute buckets are 12:26 to 12:30, the last 30 are 12:00 to 12:30.
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 12, 30, 20, TimeSpan.Zero);

    private static readonly TelemetryBlock _server = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");

    private readonly FakeMetricSearch _store = new();

    [Fact]
    public async Task ListDatabasesAsync_SqlServer_HasWhatTheRulesJudge()
    {
        // Blocked sessions: 2, 3, 2, 1, 4 over the last five minutes.
        Add("sqlserver.processes.blocked", [], minute => minute switch { 26 => 2, 27 => 3, 28 => 2, 29 => 1, _ => 4 });

        // One deadlock a minute from 12:05 to 12:10: six within 30 minutes, none within 15. The receiver sends SQL
        // Server's running total, here 40 before 12:05.
        Add("sqlserver.deadlock.rate", [], minute => 40 + Math.Clamp(minute - 4, 0, 6));
        Add("sqlserver.query.duration.max", [("database_name", "shop")], minute => minute == 28 ? 900 : 30);
        Add("sqlserver.database.backup.age", [("database_name", "shop")], _ => 31 * 3600);
        Add("sqlserver.agent.job.failed", [("job_name", "Purge sessions")], _ => 1);

        var server = Assert.Single(await new MetricHealth(_store).ListDatabasesAsync(_now, default));

        Assert.Equal((_server, "production"), (server.Server, server.Environment));
        Assert.Equal((1d, 4d), (server.BlockedLeast, server.BlockedMost));
        Assert.Equal((0d, 6d), (server.DeadlocksQuarter, server.DeadlocksHalfHour));
        var shop = Assert.Single(server.Databases);
        Assert.Equal(("shop", 30d, 900d, 31d * 3600), (shop.Name, shop.LongestQuerySeconds, shop.LongestQueryRecentMax, shop.BackupAgeSeconds));
        Assert.True(Assert.Single(server.Jobs).Failed);
    }

    [Fact]
    public async Task ListDatabasesAsync_Postgres_HasWhatTheRulesJudge()
    {
        var server = TelemetryBlock.Database("postgresql", "pg-main");

        // The receiver names the database on the resource; sqlquery names it on the point.
        Add("postgresql.backends", [], _ => 60, server, [("postgresql.database.name", "shop")]);
        Add("postgresql.backends", [], _ => 26, server, [("postgresql.database.name", "reporting")]);
        Add("postgresql.connection.max", [], _ => 100, server);
        Add("postgresql.sessions.blocked", [], minute => minute == 27 ? 1 : 2, server);

        // One deadlock a minute in shop from 12:05 to 12:10: six within 30 minutes, none within 15.
        Add(
            "postgresql.deadlocks",
            [],
            minute => 40 + Math.Clamp(minute - 4, 0, 6),
            server,
            [("postgresql.database.name", "shop")]);
        Add("postgresql.query.duration.max", [("postgresql.database.name", "shop")], m => m == 28 ? 900 : 30, server);
        Add("postgresql.database.xid.age", [("postgresql.database.name", "shop")], _ => 1_073_741_824, server);
        Add("postgresql.database.xid.age", [("postgresql.database.name", "reporting")], _ => 214_748_365, server);

        var health = Assert.Single(await new MetricHealth(_store).ListDatabasesAsync(_now, default));

        Assert.Equal(server, health.Server);
        Assert.Equal(0.86, health.ConnectionsRatio!.Value, 6);
        Assert.Equal((1d, 2d), (health.BlockedLeast, health.BlockedMost));
        Assert.Equal((0d, 6d), (health.DeadlocksQuarter, health.DeadlocksHalfHour));
        Assert.Equal(["reporting", "shop"], health.Databases.Select(database => database.Name));
        var shop = health.Databases[1];
        Assert.Equal((30d, 900d, 0.5), (shop.LongestQuerySeconds, shop.LongestQueryRecentMax, shop.WraparoundRatio));
        Assert.Null(shop.BackupAgeSeconds);
        Assert.Equal(0.1, health.Databases[0].WraparoundRatio!.Value, 6);
        Assert.Empty(health.Jobs);
    }

    [Fact]
    public async Task ListDatabasesAsync_SqlServer_HasNoConnectionsRatio()
    {
        Add("sqlserver.processes.blocked", [], _ => 0);

        Assert.Null(Assert.Single(await new MetricHealth(_store).ListDatabasesAsync(_now, default)).ConnectionsRatio);
    }

    [Fact]
    public async Task ListDatabasesAsync_DeadlocksSinceSqlServerStarted_AreNotNewDeadlocks()
    {
        // A running total that stands still: deadlocks from before the window, which the rule must not judge again.
        Add("sqlserver.deadlock.rate", [], _ => 3);

        var server = Assert.Single(await new MetricHealth(_store).ListDatabasesAsync(_now, default));

        Assert.Equal((0d, 0d), (server.DeadlocksQuarter, server.DeadlocksHalfHour));
    }

    [Fact]
    public async Task ListDatabasesAsync_ServerSendingForTwoMinutes_HasNotBeenBlockedThroughout()
    {
        Add("sqlserver.processes.blocked", [], minute => minute >= 29 ? 3 : double.NaN);

        var server = Assert.Single(await new MetricHealth(_store).ListDatabasesAsync(_now, default));

        Assert.Equal((0d, 3d), (server.BlockedLeast, server.BlockedMost));
    }

    [Fact]
    public async Task ListDatabasesAsync_HostMetricsOnly_IsEmpty()
    {
        Add("sqlserver.processes.blocked", [], _ => 1, new TelemetryBlock(AlertBlockKind.Host, "web-01"));

        Assert.Empty(await new MetricHealth(_store).ListDatabasesAsync(_now, default));
    }

    private void Add(
        string name,
        (string Key, string Value)[] attributes,
        Func<int, double> valueAtMinute,
        TelemetryBlock? block = null,
        (string Key, string Value)[]? resource = null)
    {
        var series = _store.Add(new MetricSeries(
            0,
            name,
            MetricKind.Gauge,
            "1",
            string.Empty,
            MetricTemporality.Unspecified,
            false,
            null,
            "production",
            null,
            (resource ?? []).ToDictionary(pair => pair.Key, pair => pair.Value),
            attributes.ToDictionary(pair => pair.Key, pair => pair.Value),
            _now.AddDays(-1),
            _now,
            block ?? _server));

        for (var minute = 0; minute <= 30; minute++)
        {
            var value = valueAtMinute(minute);

            if (!double.IsNaN(value))
            {
                _store.Point(series, new DateTimeOffset(2026, 11, 10, 12, minute, 0, TimeSpan.Zero), value);
            }
        }
    }
}
