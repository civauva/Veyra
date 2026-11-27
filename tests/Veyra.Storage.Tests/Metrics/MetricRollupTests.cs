using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Core.Metrics;
using Veyra.Storage.Metrics;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Metrics;

/// <summary>
/// Reads from the rollups (ADR 0015) must give exactly what the points give, so a chart looks the same whichever level
/// it is read from.
/// </summary>
[Collection(PostgresGroup.Name)]
public class MetricRollupTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _day = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset _started = _day.AddHours(10);

    private static readonly DateTimeOffset _now = _day.AddHours(13).AddMinutes(10).AddSeconds(30);

    private readonly FakeTimeProvider _clock = new(_now);

    public static TheoryData<int, int, int> Reads => new()
    {
        // From the start, in minutes after 10:00; the end, in minutes after 10:00 (-1 for now); the bucket in minutes.
        { 0, -1, 60 },
        { 0, -1, 15 },
        { 5, 120, 5 },
        { 0, 190, 1 },
        { 60, 125, 60 },

        // One bucket over the whole read, as a range's totals are (VEY-65): 10:00 to now, and 10:05 to 12:00.
        { 0, -1, 191 },
        { 5, 120, 115 },
    };

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task ReadBucketsAsync_AfterRollingUp_GivesWhatThePointsGive(int fromMinute, int toMinute, int bucket)
    {
        await using var database = await CreateAsync();
        var store = Store(database);
        var ids = await WriteHoursAsync(store);
        var from = _started.AddMinutes(fromMinute);
        var to = toMinute < 0 ? _now : _started.AddMinutes(toMinute);
        var size = TimeSpan.FromMinutes(bucket);
        var fromPoints = await store.ReadBucketsAsync(ids, from, to, size, default);

        await Rollups(database).RollUpAsync(_now, default);
        var fromRollups = await store.ReadBucketsAsync(ids, from, to, size, default);

        AssertSame(fromPoints, fromRollups);
    }

    [Fact]
    public async Task RollUpAsync_FinishedBuckets_AdvancesToTheLastFinishedFiveMinutesAndHour()
    {
        await using var database = await CreateAsync();
        await WriteHoursAsync(Store(database));

        await Rollups(database).RollUpAsync(_now, default);

        // Buckets are rolled up once they are a little older than their end, so points sent late still make it.
        Assert.Equal(
            (_day.AddHours(13).AddMinutes(5), _day.AddHours(13)),
            await StateAsync(database));
        // Three series: 10:00 to 13:05 is 37 buckets of 5 minutes, and three whole hours.
        Assert.Equal(3L * 37, await ScalarAsync(database, "SELECT count(*) FROM storage.metric_rollup_5m"));
        Assert.Equal(3L * 3, await ScalarAsync(database, "SELECT count(*) FROM storage.metric_rollup_1h"));
    }

    [Fact]
    public async Task WriteAsync_LatePointInARolledUpHour_IsReadAtOnceAndRolledUpAgain()
    {
        await using var database = await CreateAsync();
        var store = Store(database);
        var ids = await WriteHoursAsync(store);
        var rollups = Rollups(database);
        await rollups.RollUpAsync(_now, default);

        // A Collector sending its queue after an outage: a point for half past ten arrives at ten past one.
        await store.WriteAsync([Load(99, _started.AddMinutes(30).AddSeconds(10))], default);
        var late = await store.ReadBucketsAsync(ids, _started, _now, TimeSpan.FromHours(1), default);
        await rollups.RollUpAsync(_now.AddMinutes(1), default);
        var rolledAgain = await store.ReadBucketsAsync(ids, _started, _now, TimeSpan.FromHours(1), default);

        var load = late.Single(bucket => bucket.SeriesId == ids[0] && bucket.Start == _started);
        Assert.Equal(181, load.Points);
        Assert.Equal(0L, await ScalarAsync(database, "SELECT count(*) FROM storage.metric_rollup_dirty"));
        AssertSame(late, rolledAgain);
    }

    [Fact]
    public async Task ReadBucketsAsync_TwoPointsAtOneInstant_CountsBothOnce()
    {
        // A sender that retries can store the same point twice; the bucket must not be counted twice for it.
        await using var database = await CreateAsync();
        var store = Store(database);
        await store.WriteAsync([Load(3, _started.AddMinutes(1)), Load(5, _started.AddMinutes(1))], default);
        var ids = (await store.FindSeriesAsync(new MetricSeriesQuery(_day), default)).Select(s => s.Id).ToList();

        var bucket = Assert.Single(await store.ReadBucketsAsync(ids, _started, _now, TimeSpan.FromHours(1), default));

        Assert.Equal((2, 4d), (bucket.Points, bucket.Average));
    }

    [Fact]
    public async Task CreateReadCommand_SevenDaysByHour_ReadsTheHourlyRollupsAndOnlyTheNewestPoints()
    {
        await using var database = await CreateAsync();
        var partitions = new MetricPartitions(database, _clock, NullLogger<MetricPartitions>.Instance);

        for (var day = -8; day <= 1; day++)
        {
            await partitions.EnsureAsync(_day.AddDays(day), default);
        }

        var segments = MetricReadPlan.Split(
            _day.AddDays(-7),
            _now,
            TimeSpan.FromHours(1),
            fiveMinutesUpTo: _day.AddHours(13).AddMinutes(5),
            hoursUpTo: _day.AddHours(13),
            unrolled: []);
        await using var command = MetricStore.CreateReadCommand(database, [1, 2, 3], TimeSpan.FromHours(1), segments);

        var plan = await QueryPlan.ExplainAsync(database, command);

        Assert.Equal(
            [
                "metric_point_20261109",
                "metric_rollup_1h_20261102",
                "metric_rollup_1h_20261103",
                "metric_rollup_1h_20261104",
                "metric_rollup_1h_20261105",
                "metric_rollup_1h_20261106",
                "metric_rollup_1h_20261107",
                "metric_rollup_1h_20261108",
                "metric_rollup_1h_20261109",
                "metric_rollup_5m_20261109",
            ],
            plan.Relations.Distinct().Order());
    }

    /// <summary>
    /// Writes a gauge, a cumulative sum, and a histogram every 20 seconds from 10:00 to now, after the job took over
    /// at 10:00. Returns their series IDs, the gauge first.
    /// </summary>
    private static async Task<long[]> WriteHoursAsync(MetricStore store)
    {
        var points = new List<MetricPoint>();
        var index = 0;

        for (var at = _started; at < _now; at = at.AddSeconds(20), index++)
        {
            points.Add(Load((index * 7) % 23, at));
            points.Add(Requests(index * 3, at));
            points.Add(Durations(index, at));
        }

        await store.WriteAsync(points, default);

        var series = await store.FindSeriesAsync(new MetricSeriesQuery(_day), default);

        return [.. series.OrderBy(s => s.Kind).Select(s => s.Id)];
    }

    private static void AssertSame(IReadOnlyList<MetricBucket> expected, IReadOnlyList<MetricBucket> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        foreach (var (want, got) in expected.Zip(actual))
        {
            Assert.Equal(
                (want.SeriesId, want.Start, want.Points, want.LastTimestamp, want.LastStart, want.LastValue),
                (got.SeriesId, got.Start, got.Points, got.LastTimestamp, got.LastStart, got.LastValue));
            Assert.Equal(want.Average ?? double.NaN, got.Average ?? double.NaN, 9);
            Assert.Equal(want.Total ?? double.NaN, got.Total ?? double.NaN, 9);
            Assert.Equal(want.LastHistogram?.Count, got.LastHistogram?.Count);
            Assert.Equal(want.LastHistogram?.Sum, got.LastHistogram?.Sum);
            Assert.Equal(want.LastHistogram?.Bounds ?? [], got.LastHistogram?.Bounds ?? []);
            Assert.Equal(want.LastHistogram?.Counts ?? [], got.LastHistogram?.Counts ?? []);
        }
    }

    private async Task<NpgsqlDataSource> CreateAsync()
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());

        // As if the rollup job had taken over at 10:00, so the points written after it are the job's to roll up.
        await using var command = database.CreateCommand(
            "UPDATE storage.metric_rollup_state SET five_minutes = $1, hours = $1");
        command.Parameters.AddWithValue(_started);
        await command.ExecuteNonQueryAsync();

        return database;
    }

    private MetricStore Store(NpgsqlDataSource database) => new(
        database,
        new TestOptionsMonitor<StorageOptions>(new StorageOptions()),
        _clock,
        NullLogger<MetricStore>.Instance);

    private static MetricRollups Rollups(NpgsqlDataSource database) =>
        new(database, NullLogger<MetricRollups>.Instance);

    private static async Task<(DateTimeOffset FiveMinutes, DateTimeOffset Hours)> StateAsync(NpgsqlDataSource database)
    {
        await using var command = database.CreateCommand("SELECT five_minutes, hours FROM storage.metric_rollup_state");
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (reader.GetFieldValue<DateTimeOffset>(0), reader.GetFieldValue<DateTimeOffset>(1));
    }

    private static async Task<object?> ScalarAsync(NpgsqlDataSource database, string sql)
    {
        await using var command = database.CreateCommand(sql);

        return await command.ExecuteScalarAsync();
    }

    private static Dictionary<string, string> Host() =>
        new() { ["host.name"] = "web-01", ["deployment.environment.name"] = "production" };

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
        Host(),
        new Dictionary<string, string>(),
        at,
        null,
        value,
        null);

    private static MetricPoint Requests(double value, DateTimeOffset at) => new(
        "http.server.requests",
        MetricKind.Sum,
        "{request}",
        string.Empty,
        MetricTemporality.Cumulative,
        true,
        "orders",
        "production",
        "web-01",
        Host(),
        new Dictionary<string, string>(),
        at,
        _day,
        value,
        null);

    private static MetricPoint Durations(int index, DateTimeOffset at) => new(
        "http.server.request.duration",
        MetricKind.Histogram,
        "s",
        string.Empty,
        MetricTemporality.Cumulative,
        false,
        "orders",
        "production",
        "web-01",
        Host(),
        new Dictionary<string, string>(),
        at,
        _day,
        null,
        new HistogramValue(index * 4L, index * 1.5, 0.01, 2, [0.1, 1], [index * 2L, index, index]));
}
