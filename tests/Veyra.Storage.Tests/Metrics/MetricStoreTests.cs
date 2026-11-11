using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Core.Metrics;
using Veyra.Storage.Metrics;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Metrics;

[Collection(PostgresGroup.Name)]
public class MetricStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(_noon);

    [Fact]
    public async Task WriteAsync_ThenFindSeries_ReturnsOneSeriesPerAttributeSet()
    {
        await using var database = await CreateAsync();
        var store = Store(database);

        var result = await store.WriteAsync(
            [
                Cpu("user", 10, _noon.AddMinutes(-2)),
                Cpu("user", 20, _noon.AddMinutes(-1)),
                Cpu("system", 5, _noon.AddMinutes(-1)),
            ],
            CancellationToken.None);
        var series = await store.FindSeriesAsync(new MetricSeriesQuery(_noon.AddHours(-1)), default);

        Assert.Equal(new MetricWriteResult(3, 0), result);
        Assert.Equal(2, series.Count);
        var user = series.Single(s => s.Attributes["state"] == "user");
        Assert.Equal(("system.cpu.time", MetricKind.Sum, "s"), (user.Name, user.Kind, user.Unit));
        Assert.Equal((MetricTemporality.Cumulative, true), (user.Temporality, user.Monotonic));
        Assert.Equal((null, "production", "web-01"), (user.ServiceName, user.Environment, user.HostName));
        Assert.Equal("web-01", user.ResourceAttributes["host.name"]);
        Assert.Equal((_noon, _noon), (user.FirstSeen, user.LastSeen));
    }

    [Fact]
    public async Task FindSeriesAsync_Filters_MatchNameServiceEnvironmentAndHost()
    {
        await using var database = await CreateAsync();
        var store = Store(database);
        await store.WriteAsync(
            [
                Cpu("user", 1, _noon),
                Cpu("user", 1, _noon) with { HostName = "db-01", ResourceAttributes = Host("db-01") },
                Gauge("dotnet.gc.heap", 1, _noon) with { ServiceName = "orders", Environment = "staging" },
            ],
            CancellationToken.None);
        var since = _noon.AddHours(-1);

        var byName = await store.FindSeriesAsync(new MetricSeriesQuery(since, Names: ["dotnet.gc.heap"]), default);
        var byHost = await store.FindSeriesAsync(new MetricSeriesQuery(since, HostName: "db-01"), default);
        var byService = await store.FindSeriesAsync(new MetricSeriesQuery(since, ServiceName: "orders", Environment: "staging"), default);
        var notSeen = await store.FindSeriesAsync(new MetricSeriesQuery(_noon.AddMinutes(1)), default);

        Assert.Single(byName);
        Assert.Equal("db-01", Assert.Single(byHost).HostName);
        Assert.Equal("orders", Assert.Single(byService).ServiceName);
        Assert.Empty(notSeen);
    }

    [Fact]
    public async Task ReadBucketsAsync_SummarisesEachBucketWithItsLastPoint()
    {
        await using var database = await CreateAsync();
        var store = Store(database);
        await store.WriteAsync(
            [
                Gauge("load", 1, _noon.AddMinutes(-9)),
                Gauge("load", 3, _noon.AddMinutes(-7)),
                Gauge("load", 8, _noon.AddMinutes(-2)),
            ],
            CancellationToken.None);
        var id = Assert.Single(await store.FindSeriesAsync(new MetricSeriesQuery(_noon.AddHours(-1)), default)).Id;

        var buckets = await store.ReadBucketsAsync([id], _noon.AddMinutes(-10), _noon, TimeSpan.FromMinutes(5), default);

        Assert.Equal(2, buckets.Count);
        Assert.Equal((_noon.AddMinutes(-10), 2, 2d, 4d), (buckets[0].Start, buckets[0].Points, buckets[0].Average, buckets[0].Total));
        Assert.Equal((_noon.AddMinutes(-7), 3d), (buckets[0].LastTimestamp, buckets[0].LastValue));
        Assert.Equal((_noon.AddMinutes(-5), 1, 8d), (buckets[1].Start, buckets[1].Points, buckets[1].LastValue));
    }

    [Fact]
    public async Task ReadBucketsAsync_Histogram_KeepsTheLastDistributionAndStart()
    {
        await using var database = await CreateAsync();
        var store = Store(database);
        var histogram = new HistogramValue(10, 3.5, 0.01, 2, [0.1, 1], [6, 3, 1]);
        await store.WriteAsync([Durations(histogram, _noon.AddMinutes(-1))], CancellationToken.None);
        var id = Assert.Single(await store.FindSeriesAsync(new MetricSeriesQuery(_noon.AddHours(-1)), default)).Id;

        var bucket = Assert.Single(await store.ReadBucketsAsync([id], _noon.AddMinutes(-5), _noon, TimeSpan.FromMinutes(5), default));

        Assert.Null(bucket.LastValue);
        Assert.Equal(_noon.AddHours(-1), bucket.LastStart);
        var stored = bucket.LastHistogram!;
        Assert.Equal((10L, 3.5, 0.01, 2d), (stored.Count, stored.Sum, stored.Min, stored.Max));
        Assert.Equal([0.1, 1], stored.Bounds);
        Assert.Equal([6L, 3, 1], stored.Counts);
    }

    [Fact]
    public async Task WriteAsync_SeriesLimitReached_DropsPointsOfNewSeriesButKeepsExistingOnes()
    {
        await using var database = await CreateAsync();
        var store = Store(database, maxSeries: 2);
        await store.WriteAsync([Cpu("user", 1, _noon), Cpu("system", 1, _noon)], CancellationToken.None);

        var result = await store.WriteAsync(
            [Cpu("user", 2, _noon.AddSeconds(30)), Cpu("idle", 9, _noon.AddSeconds(30))],
            CancellationToken.None);

        Assert.Equal(new MetricWriteResult(1, 1), result);
        Assert.Equal(2, (await store.FindSeriesAsync(new MetricSeriesQuery(_noon.AddHours(-1)), default)).Count);
    }

    [Fact]
    public async Task WriteAsync_SecondStoreOnTheSameDatabase_FindsTheExistingSeries()
    {
        await using var database = await CreateAsync();
        await Store(database).WriteAsync([Cpu("user", 1, _noon)], CancellationToken.None);

        await Store(database).WriteAsync([Cpu("user", 2, _noon.AddMinutes(1))], CancellationToken.None);

        Assert.Single(await Store(database).FindSeriesAsync(new MetricSeriesQuery(_noon.AddHours(-1)), default));
    }

    [Fact]
    public async Task WriteAsync_RefreshesLastSeenAtMostOnceAMinute()
    {
        await using var database = await CreateAsync();
        var store = Store(database);
        await store.WriteAsync([Cpu("user", 1, _noon)], CancellationToken.None);

        _clock.Advance(TimeSpan.FromSeconds(30));
        await store.WriteAsync([Cpu("user", 2, _noon)], CancellationToken.None);
        var unchanged = Assert.Single(await store.FindSeriesAsync(new MetricSeriesQuery(_noon.AddHours(-1)), default));
        _clock.Advance(TimeSpan.FromSeconds(31));
        await store.WriteAsync([Cpu("user", 3, _noon)], CancellationToken.None);
        var refreshed = Assert.Single(await store.FindSeriesAsync(new MetricSeriesQuery(_noon.AddHours(-1)), default));

        Assert.Equal(_noon, unchanged.LastSeen);
        Assert.Equal(_noon.AddSeconds(61), refreshed.LastSeen);
    }

    [Fact]
    public async Task ListNamesAsync_GroupsSeriesByNameWithTheirSources()
    {
        await using var database = await CreateAsync();
        var store = Store(database);
        await store.WriteAsync(
            [
                Cpu("user", 1, _noon),
                Cpu("system", 1, _noon),
                Gauge("http_server", 1, _noon) with { ServiceName = "orders" },
            ],
            CancellationToken.None);

        var all = await store.ListNamesAsync(null, _noon.AddHours(-1), CancellationToken.None);
        var searched = await store.ListNamesAsync("CPU", _noon.AddHours(-1), CancellationToken.None);
        var wildcard = await store.ListNamesAsync("_", _noon.AddHours(-1), CancellationToken.None);

        Assert.Equal(["http_server", "system.cpu.time"], all.Select(name => name.Name));
        var cpu = Assert.Single(searched);
        Assert.Equal((MetricKind.Sum, "s", 2), (cpu.Kind, cpu.Unit, cpu.Series));
        Assert.Equal(["web-01"], cpu.Sources);
        Assert.Equal("http_server", Assert.Single(wildcard).Name);
    }

    [Fact]
    public async Task GetUsageAsync_CountsSeriesRecentPointsAndBytes()
    {
        await using var database = await CreateAsync();
        var store = Store(database);
        await store.WriteAsync([Cpu("user", 1, _noon), Cpu("system", 1, _noon)], CancellationToken.None);

        var usage = await store.GetUsageAsync(_noon.AddMinutes(1), CancellationToken.None);
        var later = await store.GetUsageAsync(_noon.AddMinutes(10), CancellationToken.None);

        Assert.Equal((2, 50_000, 30), (usage.Series, usage.MaxSeries, usage.RetentionDays));
        Assert.Equal(2 / 300d, usage.PointsPerSecond);
        Assert.True(usage.UsedBytes > 0);
        Assert.Equal(0, later.PointsPerSecond);
    }

    private Task<NpgsqlDataSource> CreateAsync() => postgres.CreateMigratedDatabaseAsync(new StorageMigrations());

    private MetricStore Store(NpgsqlDataSource database, int maxSeries = 50_000) => new(
        database,
        Options.Create(new StorageOptions { MaxMetricSeries = maxSeries }),
        _clock,
        NullLogger<MetricStore>.Instance);

    private static Dictionary<string, string> Host(string host = "web-01") =>
        new() { ["host.name"] = host, ["deployment.environment.name"] = "production" };

    private static MetricPoint Cpu(string state, double value, DateTimeOffset at) => new(
        Name: "system.cpu.time",
        Kind: MetricKind.Sum,
        Unit: "s",
        Description: "CPU time",
        Temporality: MetricTemporality.Cumulative,
        Monotonic: true,
        ServiceName: null,
        Environment: "production",
        HostName: "web-01",
        ResourceAttributes: Host(),
        Attributes: new Dictionary<string, string> { ["state"] = state, ["cpu"] = "0" },
        Timestamp: at,
        StartTimestamp: _noon.AddDays(-1),
        Value: value,
        Histogram: null);

    private static MetricPoint Gauge(string name, double value, DateTimeOffset at) => new(
        Name: name,
        Kind: MetricKind.Gauge,
        Unit: "1",
        Description: string.Empty,
        Temporality: MetricTemporality.Unspecified,
        Monotonic: false,
        ServiceName: null,
        Environment: "production",
        HostName: "web-01",
        ResourceAttributes: Host(),
        Attributes: new Dictionary<string, string>(),
        Timestamp: at,
        StartTimestamp: null,
        Value: value,
        Histogram: null);

    private static MetricPoint Durations(HistogramValue histogram, DateTimeOffset at) => new(
        Name: "http.server.request.duration",
        Kind: MetricKind.Histogram,
        Unit: "s",
        Description: string.Empty,
        Temporality: MetricTemporality.Cumulative,
        Monotonic: false,
        ServiceName: "orders",
        Environment: "production",
        HostName: "web-01",
        ResourceAttributes: Host(),
        Attributes: new Dictionary<string, string> { ["http.route"] = "/orders" },
        Timestamp: at,
        StartTimestamp: _noon.AddHours(-1),
        Value: null,
        Histogram: histogram);
}
