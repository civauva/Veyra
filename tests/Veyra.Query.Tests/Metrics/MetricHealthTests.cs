using Veyra.Core.Metrics;
using Veyra.Query.Metrics;

namespace Veyra.Query.Tests.Metrics;

public class MetricHealthTests
{
    // 07:34:12 UTC, so the current hour starts at 07:00.
    private static readonly DateTimeOffset _now = new(2026, 11, 7, 7, 34, 12, TimeSpan.Zero);
    private static readonly DateTimeOffset _hour = new(2026, 11, 7, 7, 0, 0, TimeSpan.Zero);

    private readonly FakeMetricSearch _store = new();

    [Fact]
    public async Task ListDisksAsync_ReturnsEachDiskWithItsShareAndForecast()
    {
        FillingDisk();

        var disk = Assert.Single(await new MetricHealth(_store).ListDisksAsync(_now, CancellationToken.None));

        Assert.Equal(("web-01", null, "/", 80e9, 100e9, 0.8), (disk.Host, disk.Environment, disk.Mountpoint, disk.UsedBytes, disk.TotalBytes, disk.Ratio));
        Assert.InRange(disk.DaysUntilFull!.Value, 19.5, 20.5);
    }

    [Fact]
    public async Task ListDisksAsync_WithoutHostMetrics_IsEmpty()
    {
        Assert.Empty(await new MetricHealth(_store).ListDisksAsync(_now, CancellationToken.None));
    }

    [Fact]
    public async Task ListLatenciesAsync_ReturnsTheP95AndTheRequestsOfEachService()
    {
        var minute = new DateTimeOffset(2026, 11, 7, 7, 28, 0, TimeSpan.Zero);
        var fast = _store.Add(MetricTestData.Durations("orders", 200, _now));
        var slow = _store.Add(MetricTestData.Durations("payments", 200, _now));
        MetricTestData.Grow(_store, fast, minute, 7, [100, 0, 0]);
        MetricTestData.Grow(_store, slow, minute, 7, [50, 48, 2]);

        var latencies = (await new MetricHealth(_store).ListLatenciesAsync(_now, CancellationToken.None))
            .OrderBy(latency => latency.Service, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["orders", "payments"], latencies.Select(latency => latency.Service));
        Assert.All(latencies, latency => Assert.Equal("production", latency.Environment));
        Assert.Equal(500, latencies[0].Requests);
        Assert.True(latencies[0].P95Seconds < 0.1);
        Assert.Equal(500, latencies[1].Requests);
        Assert.True(latencies[1].P95Seconds > 0.5);
    }

    [Fact]
    public async Task ReadDiskAsync_ReturnsTheShareInUsePerBucket()
    {
        FillingDisk();

        var history = await new MetricHealth(_store).ReadDiskAsync(
            "web-01",
            "/",
            _hour.AddDays(-1),
            _now,
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(15), history.Bucket);
        Assert.Equal(_hour.AddDays(-1), history.Points[0].At);
        Assert.Equal(0.79, history.Points[0].Ratio!.Value, 3);
        Assert.Equal(0.8, history.Points[^1].Ratio!.Value, 3);
    }

    [Fact]
    public async Task ReadDiskAsync_OtherMountPoint_HasNoValues()
    {
        FillingDisk();

        var history = await new MetricHealth(_store).ReadDiskAsync(
            "web-01",
            "/boot",
            _hour.AddDays(-1),
            _now,
            CancellationToken.None);

        Assert.All(history.Points, point => Assert.Null(point.Ratio));
    }

    [Fact]
    public async Task ReadLatencyAsync_ReturnsPercentilesPerMinuteAndTheSlowestRouteFirst()
    {
        var minute = new DateTimeOffset(2026, 11, 7, 7, 20, 0, TimeSpan.Zero);
        var cart = _store.Add(MetricTestData.Durations("checkout-web", 200, _now, "/cart"));
        var products = _store.Add(MetricTestData.Durations("checkout-web", 200, _now, "/products/{id}"));
        MetricTestData.Grow(_store, cart, minute, 15, [0, 10, 30]);
        MetricTestData.Grow(_store, products, minute, 15, [60, 0, 0]);

        var history = await new MetricHealth(_store).ReadLatencyAsync(
            "checkout-web",
            "production",
            minute.AddMinutes(1),
            _now,
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(1), history.Bucket);
        var point = history.Points.First(one => one.Requests > 0);
        Assert.Equal(100, point.Requests);
        Assert.True(point.P95Seconds > point.P50Seconds);
        Assert.Equal(["/cart", "/products/{id}"], history.Routes.Select(route => route.Route));
    }

    [Fact]
    public void Covering_PicksTheShortestBucketWithinThePointLimit()
    {
        var start = new DateTimeOffset(2026, 11, 7, 6, 28, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMinutes(1), MetricWindow.Covering(start, start.AddHours(1), 240).Bucket);
        Assert.Equal(TimeSpan.FromMinutes(5), MetricWindow.Covering(start, start.AddHours(10), 240).Bucket);
        Assert.Equal(TimeSpan.FromHours(1), MetricWindow.Covering(start, start.AddDays(8), 240).Bucket);
        Assert.Equal(start, MetricWindow.Covering(start, start.AddHours(1), 240).Start);
    }

    // 100 GB, 80 % used now, growing by 1 % of the disk a day.
    private void FillingDisk()
    {
        var used = _store.Add(MetricTestData.HostSeries(
            "system.filesystem.usage",
            MetricKind.Sum,
            false,
            _now,
            ("mountpoint", "/"),
            ("state", "used"),
            ("type", "ext4")));
        var free = _store.Add(MetricTestData.HostSeries(
            "system.filesystem.usage",
            MetricKind.Sum,
            false,
            _now,
            ("mountpoint", "/"),
            ("state", "free"),
            ("type", "ext4")));

        for (var hour = 0; hour <= 24; hour++)
        {
            var usedBytes = 80e9 - (hour * 1e9 / 24);
            _store.Point(used, _hour.AddHours(-hour), usedBytes);
            _store.Point(free, _hour.AddHours(-hour), 100e9 - usedBytes);
        }

        for (var minute = 29; minute <= 34; minute++)
        {
            _store.Point(used, _hour.AddMinutes(minute), 80e9);
            _store.Point(free, _hour.AddMinutes(minute), 20e9);
        }
    }
}
