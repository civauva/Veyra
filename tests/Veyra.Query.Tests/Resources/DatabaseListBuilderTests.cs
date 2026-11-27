using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Home;
using Veyra.Query.Resources;
using Veyra.Query.Tests.Home;
using Veyra.Query.Tests.Live;
using Veyra.Query.Tests.Metrics;

namespace Veyra.Query.Tests.Resources;

public class DatabaseListBuilderTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 26, 12, 4, 20, TimeSpan.Zero);
    private static readonly DateTimeOffset _first = new(2026, 11, 26, 11, 4, 0, TimeSpan.Zero);
    private static readonly TelemetryBlock _sql = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");
    private static readonly TelemetryBlock _postgres = TelemetryBlock.Database("postgresql", "pg-main");
    private static readonly TelemetryBlock _old = TelemetryBlock.Database("microsoft.sql_server", "sql-old");

    private readonly FakeMetricSearch _store = new();
    private readonly FakeAlertStates _alerts = new();
    private readonly OneRead _activity = new();

    public DatabaseListBuilderTests()
    {
        Level(_sql, "sqlserver.server.info", 1, ("version", "16.0.4135.4"), ("update_level", "CU14"));
        Level(_sql, "sqlserver.cpu.sql.ratio", 0.28);
        Level(_sql, "sqlserver.database.size", 10e9, ("database_name", "shop"));
        Level(_sql, "sqlserver.database.size", 5e9, ("database_name", "Reporting"));
        Level(_sql, "sqlserver.database.size", 1e6, ("database_name", "master"));
        Level(_sql, "sqlserver.database.backup.age", 21_600, ("database_name", "shop"));
        Level(_sql, "sqlserver.database.backup.age", 200_000, ("database_name", "Reporting"));
        Level(_sql, "sqlserver.database.backup.age", 1_000, ("database_name", "master"));
        Level(_postgres, "postgresql.db_size", 2e9, ("postgresql.database.name", "app"));
        _store.Add(Series(_old, "sqlserver.server.info") with { LastSeen = _now.AddHours(-2) });
        _activity.Sessions = LiveSamples.Chain();

        _alerts.Alerts.Add(new AlertState(
            Guid.NewGuid(),
            "database-backup-age",
            null,
            AlertSubject.Database(_sql.Name, "Reporting"),
            AlertSeverity.Caution,
            55,
            55,
            26,
            TimeSpan.FromMinutes(5),
            _now,
            _now,
            null));
    }

    [Fact]
    public async Task ReadAsync_StorageRead_GivesEachDatabaseItsStateWithoutCountingItAsADatabase()
    {
        Level(_sql, "sqlserver.database.offline", 0, ("database_name", "shop"), ("state", "ONLINE"));
        Level(_sql, "sqlserver.database.offline", 1, ("database_name", "Reporting"), ("state", "RECOVERY_PENDING"));

        var listing = await Builder().ReadAsync(Query(), null, system: false, default);

        var sql = listing.Servers.Single(server => server.Block == _sql);
        Assert.Equal(["Reporting", "shop"], sql.States.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("RECOVERY_PENDING", sql.States["Reporting"]);
        Assert.Equal(["shop", "Reporting"], sql.Databases.Select(database => database.Name));
        Assert.Equal(["Reporting"], sql.Late.Select(database => database.Name));
        Assert.Empty(listing.Servers.Single(server => server.Block == _postgres).States);
    }

    [Fact]
    public async Task ListAsync_WorstFirst_HasEachServersDatabasesBackupsAndChains()
    {
        var list = (await Builder().ListAsync(Query(), null, system: false, default))!;

        Assert.Equal(["sql-prod-01", "sql-old", "pg-main"], list.Items.Select(item => item.Server));
        var sql = list.Items[0];
        Assert.Equal(("SQL Server 2022 CU14", BlockHealth.Caution, false), (sql.Version, sql.Health, sql.Silent));
        Assert.Equal((2, 15e9), (sql.Databases, sql.SizeBytes));
        Assert.Equal((200_000d, 1), (sql.LastFullBackupAgeSeconds, sql.LateBackups));
        Assert.Equal(1, sql.BlockingChains);
        Assert.Equal(0.28, sql.CpuRatio!.Value, 6);
        var old = list.Items[1];
        Assert.True(old.Silent);
        Assert.Null(old.BlockingChains);
        var postgres = list.Items[2];
        Assert.Equal(("postgresql", 1, 2e9), (postgres.Engine, postgres.Databases, postgres.SizeBytes));
        Assert.Null(postgres.LateBackups);
        Assert.Null(postgres.Version);
    }

    [Fact]
    public async Task ListAsync_Header_CountsServersDatabasesLateBackupsAndChains()
    {
        var header = (await Builder().ListAsync(Query(), null, system: false, default))!.Header;

        Assert.Equal((3, 1, 3), (header.Servers, header.Silent, header.Databases));
        Assert.Equal(17e9, header.DataBytes);
        Assert.Equal(1, header.LateBackups);
        Assert.Equal(("sql-prod-01", "Reporting"), (header.LateBackupFirst!.Server, header.LateBackupFirst.Database));
        Assert.Equal((1, "sql-prod-01"), (header.BlockingChains, header.BlockingServer));
    }

    [Fact]
    public async Task ListAsync_SystemEngineAndSearch_FilterOnTheServer()
    {
        var withSystem = (await Builder().ListAsync(Query(), null, system: true, default))!;
        var postgres = (await Builder().ListAsync(Query(), "postgresql", system: false, default))!;
        var found = (await Builder().ListAsync(Query(search: "OLD"), null, system: false, default))!;

        Assert.Equal(3, withSystem.Items[0].Databases);
        Assert.Equal(["pg-main"], postgres.Items.Select(item => item.Server));
        Assert.Equal(["sql-old"], found.Items.Select(item => item.Server));
    }

    private DatabaseListBuilder Builder() => new(
        _store,
        _alerts,
        _activity,
        new FixedOptions<QueryOptions>(new QueryOptions()),
        new FakeTimeProvider(_now));

    private static ResourceListQuery Query(string? search = null) =>
        new(null, "1h", ResourceHealthFilter.Parse(null)!, search, null, null, 50);

    private static MetricSeries Series(
        TelemetryBlock block,
        string name,
        params (string Key, string Value)[] attributes) =>
        new(
            0,
            name,
            MetricKind.Gauge,
            "1",
            string.Empty,
            MetricTemporality.Unspecified,
            false,
            null,
            null,
            "db-host",
            new Dictionary<string, string> { ["db.system.name"] = block.Engine!, ["server.address"] = block.Server! },
            attributes.ToDictionary(pair => pair.Key, pair => pair.Value),
            _now.AddDays(-1),
            _now,
            block);

    private void Level(TelemetryBlock block, string name, double value, params (string Key, string Value)[] attributes)
    {
        var stored = _store.Add(Series(block, name, attributes));

        for (var minute = 0; minute <= 60; minute++)
        {
            _store.Point(stored, _first.AddMinutes(minute), value);
        }
    }

    // The newest live read of sql-prod-01, with the sessions a test sets.
    private sealed class OneRead : IDatabaseActivitySearch
    {
        public IReadOnlyList<SessionSample> Sessions { get; set; } = [];

        public Task<SessionSnapshot?> GetSessionsAsync(
            string block,
            DateTimeOffset at,
            TimeSpan maxAge,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionSnapshot>> ListLatestSessionsAsync(
            DateTimeOffset now,
            TimeSpan maxAge,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionSnapshot>>(
            [
                new SessionSnapshot(
                    _sql.Name,
                    null,
                    now,
                    new ServerSample(_sql.Name, null, now, 0, 0, 0),
                    Sessions),
            ]);

        public Task<IReadOnlyList<ActivityRecord>> ListRecordsAsync(
            string block,
            string kind,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            IReadOnlyCollection<string>? keys,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ActivityRecord>> ListRecordsMatchingAsync(
            string block,
            string kind,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            ActivityAttributeMatch match,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionSample>> ListBlockingAsync(
            string block,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionCaller>> ListCallersAsync(
            string block,
            string queryHash,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
