using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Storage.Logs;
using Veyra.Storage.Metrics;
using Veyra.Testing;

namespace Veyra.Storage.Tests;

/// <summary>
/// After an upgrade, the records and points written before counts and rollups existed are counted and rolled up in
/// the background (ADR 0015). Until then their days are read from the records and points, so nothing is ever wrong.
/// </summary>
[Collection(PostgresGroup.Name)]
public class BackfillTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _day = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    private const string _daysLeft = "SELECT string_agg(day::text, ',') FROM storage.log_count_backfill";

    [Fact]
    public async Task CountNextDayAsync_DaysWrittenBeforeCounting_CountsThemNewestFirstWithoutCountingTwice()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        await store.WriteAsync(
            [Log(_day.AddDays(-1).AddHours(3)), Log(_day.AddHours(8)), Log(_day.AddHours(9))],
            default);

        // As the upgrade leaves it: the records are there, their counts are not, and both days are the backfill's.
        await ExecuteAsync(database, "DELETE FROM storage.log_count_minute; DELETE FROM storage.log_count_hour");
        await ExecuteAsync(
            database,
            """
            INSERT INTO storage.log_count_backfill (day, up_to_id)
            SELECT day, (SELECT max(id) FROM storage.log_record)
            FROM (VALUES (date '2026-11-08'), (date '2026-11-09')) AS d (day)
            """);

        // A record written after the upgrade is counted by the writer, so the backfill must leave it alone.
        await store.WriteAsync([Log(_day.AddHours(10))], default);
        var backfill = new LogCountBackfill(database);
        var before = await SummaryAsync(store);

        Assert.True(await backfill.CountNextDayAsync(default));
        Assert.Equal("2026-11-08", await ScalarAsync(database, _daysLeft));
        Assert.True(await backfill.CountNextDayAsync(default));
        Assert.False(await backfill.CountNextDayAsync(default));

        Assert.Equal(before, await SummaryAsync(store));
        Assert.Equal(4L, await ScalarAsync(database, "SELECT sum(records)::bigint FROM storage.log_count_minute"));
        Assert.Equal(4L, await ScalarAsync(database, "SELECT sum(records)::bigint FROM storage.log_count_hour"));
    }

    [Fact]
    public async Task BackfillNextDayAsync_PointsWrittenBeforeRollups_RollsThemUpAndReadsTheSame()
    {
        await using var database = await CreateAsync();
        var clock = new FakeTimeProvider(_day.AddHours(12));
        var store = new MetricStore(
            database,
            new TestOptionsMonitor<StorageOptions>(new StorageOptions()),
            clock,
            NullLogger<MetricStore>.Instance);
        var points = Enumerable.Range(0, 6 * 60)
            .Select(minute => Load(minute % 17, _day.AddHours(4).AddMinutes(minute)));
        await store.WriteAsync(points.ToList(), default);
        var id = (await store.FindSeriesAsync(new MetricSeriesQuery(_day), default))[0].Id;

        // As the upgrade leaves it: the job took over at 10:00, and the day before is the backfill's.
        await ExecuteAsync(database, "DELETE FROM storage.metric_rollup_dirty");
        await ExecuteAsync(
            database,
            """
            UPDATE storage.metric_rollup_state SET five_minutes = '2026-11-09 10:00+00', hours = '2026-11-09 10:00+00';
            INSERT INTO storage.metric_rollup_backfill (day, up_to) VALUES ('2026-11-09', '2026-11-09 10:00+00');
            """);
        var rollups = new MetricRollups(database, NullLogger<MetricRollups>.Instance);
        var before = await store.ReadBucketsAsync([id], _day, _day.AddHours(10), TimeSpan.FromHours(1), default);

        Assert.True(await rollups.BackfillNextDayAsync(default));
        Assert.False(await rollups.BackfillNextDayAsync(default));
        var after = await store.ReadBucketsAsync([id], _day, _day.AddHours(10), TimeSpan.FromHours(1), default);

        Assert.Equal(6L, await ScalarAsync(database, "SELECT count(*) FROM storage.metric_rollup_1h"));
        Assert.Equal(before.Select(Shape), after.Select(Shape));
    }

    private static (DateTimeOffset, int, double?, double) Shape(MetricBucket bucket) =>
        (bucket.Start, bucket.Points, bucket.LastValue, Math.Round(bucket.Average!.Value, 9));

    private Task<NpgsqlDataSource> CreateAsync() => postgres.CreateMigratedDatabaseAsync(new StorageMigrations());

    private static async Task<IReadOnlyList<LogBucket>> SummaryAsync(LogStore store) =>
        (await store.SummarizeAsync(
            new LogQuery(_day.AddDays(-1), _day.AddDays(1), null, null, null, null, 100, null),
            TimeSpan.FromHours(1),
            default)).Buckets;

    private static async Task ExecuteAsync(NpgsqlDataSource database, string sql)
    {
        await using var batch = database.CreateBatch();

        var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var statement in statements)
        {
            batch.BatchCommands.Add(new NpgsqlBatchCommand(statement));
        }

        await batch.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlDataSource database, string sql)
    {
        await using var command = database.CreateCommand(sql);

        return await command.ExecuteScalarAsync();
    }

    private static LogRecord Log(DateTimeOffset at) => new(
        at,
        at,
        LogSeverity.Information,
        null,
        "message",
        "payments",
        "production",
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string>());

    private static MetricPoint Load(double value, DateTimeOffset at) => new(
        "system.cpu.load_average.1m",
        MetricKind.Gauge,
        "1",
        string.Empty,
        MetricTemporality.Unspecified,
        false,
        null,
        "production",
        "web-01",
        new Dictionary<string, string> { ["host.name"] = "web-01" },
        new Dictionary<string, string>(),
        at,
        null,
        value,
        null);
}
