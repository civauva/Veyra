using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;

namespace Veyra.Query.Tests.Metrics;

/// <summary>
/// A PostgreSQL server's screen and card from the series the tested Collector configuration sends (ADR 0018): the
/// <c>postgresql</c> receiver's metrics, which name their database on the resource, and <c>sqlquery</c>'s, which name
/// it on the point.
/// </summary>
public class PostgresViewTests
{
    // 12:04:20 UTC: the last five one-minute buckets are 12:00 to 12:04, with 11:59 as their baseline.
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 12, 4, 20, TimeSpan.Zero);
    private static readonly DateTimeOffset _baseline = new(2026, 11, 10, 11, 59, 0, TimeSpan.Zero);

    private static readonly TelemetryBlock _server = TelemetryBlock.Database("postgresql", "pg-main");

    private readonly FakeMetricSearch _store = new();

    [Fact]
    public async Task DatabaseAsync_Postgres_HasConnectionsOfTheMaximumBlockingAndTransactions()
    {
        AddLevel("postgresql.backends", 60, Database("shop"));
        AddLevel("postgresql.backends", 26, Database("reporting"));
        AddLevel("postgresql.connection.max", 100);
        AddLevel("postgresql.sessions.blocked", 2);
        AddCounter("postgresql.commits", perMinute: 600, Database("shop"));
        AddCounter("postgresql.rollbacks", perMinute: 6, Database("shop"));
        AddLevel("postgresql.query.duration.max", 134, attributes: [("postgresql.database.name", "shop")]);
        AddLevel("postgresql.query.duration.max", 3, attributes: [("postgresql.database.name", "reporting")]);

        var view = (await Builder().DatabaseAsync("postgresql", "pg-main", "1h", system: true, default))!;

        var card = view.Card;
        Assert.Equal(("postgresql", "pg-main", "production"), (card.Engine, card.Server, card.Environment));
        Assert.Equal((86d, 100d, 2d), (card.Connections, card.MaxConnections, card.Blocked));
        Assert.Equal(10d, card.CommitsPerSecond!.Value, 6);
        Assert.Equal(0.1, card.RollbacksPerSecond!.Value, 6);
        Assert.Equal((134d, 2), (card.LongestQuerySeconds, card.Databases));
        Assert.Null(card.BatchRequests);
        Assert.Equal("transactions", view.Requests.Label);
        Assert.Equal(10.1, view.Requests.Points[^1].Value!.Value, 6);
        Assert.Equal(10d, view.Commits!.Points[^1].Value!.Value, 6);
        Assert.Equal(0.1, view.Rollbacks!.Points[^1].Value!.Value, 6);
        Assert.Equal(86d, view.Connections.Points[^1].Value);
        Assert.Equal(100d, view.MaxConnections!.Points[^1].Value);
        Assert.Equal(134d, view.LongestQuery.Points[^1].Value);
        Assert.Empty(view.Waits);
    }

    [Fact]
    public async Task DatabaseAsync_PostgresDeadlocks_CountTheLastFifteenMinutesOfEveryDatabase()
    {
        // One deadlock a minute in shop after the baseline; reporting's running total stands still.
        AddCounter("postgresql.deadlocks", perMinute: 1, Database("shop"));
        AddLevel("postgresql.deadlocks", 7, Database("reporting"));

        var view = (await Builder().DatabaseAsync("postgresql", "pg-main", "1h", system: true, default))!;

        Assert.Equal(5, view.Card.Deadlocks);
    }

    [Fact]
    public async Task DatabaseAsync_PostgresDatabases_HaveSizeWraparoundDeadRowsAndVacuums()
    {
        AddLevel("postgresql.db_size", 412e9, Database("shop"));
        AddLevel("postgresql.db_size", 8e9, Database("reporting"));
        AddLevel("postgresql.backends", 60, Database("shop"));
        AddLevel(
            "postgresql.database.xid.age",
            1_073_741_824,
            attributes: [("postgresql.database.name", "shop")]);
        AddLevel(
            "postgresql.database.xid.age",
            214_748_365,
            attributes: [("postgresql.database.name", "reporting")]);
        AddLevel("postgresql.rows", 900, Table("shop", "orders"), [("state", "live")]);
        AddLevel("postgresql.rows", 100, Table("shop", "orders"), [("state", "dead")]);
        AddLevel("postgresql.rows", 50, Table("shop", "carts"), [("state", "live")]);
        AddLevel("postgresql.rows", 150, Table("shop", "carts"), [("state", "dead")]);
        AddLevel("postgresql.rows", 1000, Table("reporting", "reports"), [("state", "live")]);
        AddLevel("postgresql.rows", 0, Table("reporting", "reports"), [("state", "dead")]);
        AddCounter("postgresql.table.vacuum.count", perMinute: 1, Table("shop", "carts"));
        AddLevel("postgresql.table.vacuum.count", 40, Table("shop", "orders"));

        var view = (await Builder().DatabaseAsync("postgresql", "pg-main", "1h", system: true, default))!;

        Assert.Equal(["shop", "reporting"], view.Databases.Select(database => database.Name));
        var shop = view.Databases[0];
        Assert.Equal((412e9, 60d), (shop.SizeBytes, shop.Connections));
        Assert.Equal((1_073_741_824d, 0.5), (shop.TransactionIdAge, shop.WraparoundRatio));
        Assert.Equal((950d, 250d, 5d), (shop.LiveRows, shop.DeadRows, shop.Vacuums));
        Assert.Equal(250d / 1200, shop.DeadRowsRatio!.Value, 6);
        Assert.Equal(0.1, view.Databases[1].WraparoundRatio!.Value, 6);
        Assert.Equal(0.5, view.Card.WraparoundRatio);

        Assert.Equal(
            [("shop", "public", "carts", 150d, 0.75, 5d), ("shop", "public", "orders", 100d, 0.1, 0d)],
            view.Tables.Select(table => (
                table.Database,
                table.Schema,
                table.Name,
                table.DeadRows,
                table.DeadRowsRatio,
                table.Vacuums)));
    }

    [Fact]
    public async Task DatabaseAsync_SystemHidden_ListsTheApplicationsTablesAndDatabasesOnly()
    {
        AddLevel("postgresql.db_size", 412e9, Database("shop"));
        AddLevel("postgresql.db_size", 8e6, Database("postgres"));
        AddLevel("postgresql.rows", 900, Table("shop", "orders"), [("state", "live")]);
        AddLevel("postgresql.rows", 10, Table("shop", "orders"), [("state", "dead")]);

        // Twelve catalog tables with more dead rows than the application's: the ten listed must still be its own.
        for (var index = 0; index < 12; index++)
        {
            (string, string)[] catalog =
            [
                ("postgresql.database.name", "shop"),
                ("postgresql.table.name", $"pg_catalog.pg_table_{index}"),
            ];
            AddLevel("postgresql.rows", 100, catalog, [("state", "live")]);
            AddLevel("postgresql.rows", 500, catalog, [("state", "dead")]);
        }

        var view = (await Builder().DatabaseAsync("postgresql", "pg-main", "1h", system: false, default))!;

        Assert.Equal(["shop"], view.Databases.Select(database => database.Name));
        Assert.Equal(["orders"], view.Tables.Select(table => table.Name));
        Assert.Equal(1, view.HiddenDatabases.Count);
        Assert.Equal(12, view.HiddenTables.Count);
        Assert.Empty(view.HiddenTables.Names);
    }

    [Fact]
    public async Task DatabaseAsync_PostgresDatabase_GrowsPerDay()
    {
        var size = AddSeries("postgresql.db_size", MetricKind.Sum, Database("shop"), []);
        _store.Point(size, _baseline.AddMinutes(5).AddDays(-1), 400e9);
        _store.Point(size, _baseline.AddMinutes(5), 412e9);

        var view = (await Builder().DatabaseAsync("postgresql", "pg-main", "1h", system: true, default))!;

        Assert.Equal(12e9, view.Databases.Single().GrowthBytesPerDay!.Value, 0);
    }

    [Fact]
    public async Task OverviewAsync_PostgresServer_HasACardWithItsConnectionsAndTransactions()
    {
        AddLevel("postgresql.backends", 86, Database("shop"));
        AddLevel("postgresql.connection.max", 100);
        AddCounter("postgresql.commits", perMinute: 600, Database("shop"));
        AddLevel(
            "postgresql.database.xid.age",
            1_116_691_497,
            attributes: [("postgresql.database.name", "shop")]);

        var overview = await new MetricViewBuilder(_store, new FakeTimeProvider(_now)).OverviewAsync("1h", default);

        var card = Assert.Single(overview.Databases);
        Assert.Equal(("postgresql", "pg-main"), (card.Engine, card.Server));
        Assert.Equal((86d, 100d), (card.Connections, card.MaxConnections));
        Assert.Equal(10d, card.CommitsPerSecond!.Value, 6);
        Assert.Equal(0.52, card.WraparoundRatio!.Value, 6);
        Assert.Equal("transactions", card.Requests.Label);
    }

    private DatabaseViewBuilder Builder() => new(_store, new NoEvents(), new FakeTimeProvider(_now));

    private static (string Key, string Value)[] Database(string name) => [("postgresql.database.name", name)];

    // As the Collector 0.140 receiver sends a table: its schema inside the table name, without a schema attribute.
    private static (string Key, string Value)[] Table(string database, string table) =>
    [
        ("postgresql.database.name", database),
        ("postgresql.table.name", $"public.{table}"),
    ];

    // A running total, as the receiver sends commits, rollbacks, deadlocks, and vacuums: 50,000 at the baseline.
    private void AddCounter(string name, double perMinute, (string Key, string Value)[] resource)
    {
        var series = AddSeries(name, MetricKind.Sum, resource, []);

        for (var minute = 0; minute <= 5; minute++)
        {
            _store.Point(series, _baseline.AddMinutes(minute), 50_000 + (perMinute * minute));
        }
    }

    private void AddLevel(
        string name,
        double value,
        (string Key, string Value)[]? resource = null,
        (string Key, string Value)[]? attributes = null)
    {
        var series = AddSeries(name, MetricKind.Gauge, resource ?? [], attributes ?? []);

        for (var minute = 0; minute <= 5; minute++)
        {
            _store.Point(series, _baseline.AddMinutes(minute), value);
        }
    }

    private MetricSeries AddSeries(
        string name,
        MetricKind kind,
        (string Key, string Value)[] resource,
        (string Key, string Value)[] attributes)
    {
        var resourceAttributes = new Dictionary<string, string>
        {
            ["db.system.name"] = "postgresql",
            ["server.address"] = "pg-main",
            ["deployment.environment.name"] = "production",
        };

        foreach (var (key, value) in resource)
        {
            resourceAttributes[key] = value;
        }

        return _store.Add(new MetricSeries(
            0,
            name,
            kind,
            "1",
            string.Empty,
            kind == MetricKind.Sum ? MetricTemporality.Cumulative : MetricTemporality.Unspecified,
            kind == MetricKind.Sum,
            null,
            "production",
            null,
            resourceAttributes,
            attributes.ToDictionary(pair => pair.Key, pair => pair.Value),
            _now.AddDays(-1),
            _now,
            _server));
    }

    // A PostgreSQL server sends no query samples to the screen.
    private sealed class NoEvents : IBlockEvents
    {
        public Task<IReadOnlyList<LogRecord>> ListAsync(
            TelemetryBlock block,
            string eventName,
            DateTimeOffset since,
            DateTimeOffset until,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LogRecord>>([]);
    }
}
