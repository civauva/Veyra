using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;

namespace Veyra.Query.Tests.Metrics;

public class ContainerViewTests
{
    // 12:30:20 UTC: the last 15 one-minute buckets start at 12:15, with 12:14 as their baseline.
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 12, 30, 20, TimeSpan.Zero);

    private readonly FakeMetricSearch _store = new();

    [Fact]
    public async Task ListContainersAsync_RestartingContainer_CountsRestartsOfTheLastFifteenMinutes()
    {
        // Docker's count: 3 since long ago, 4 at 12:21, 6 from 12:25. Three restarts within 15 minutes.
        Add("container.restarts", "shop-api", "web-01", minute => minute < 21 ? 3 : minute < 25 ? 4 : 6, counter: true);
        Add("container.memory.usage.total", "shop-api", "web-01", _ => 930);
        Add("container.memory.usage.limit", "shop-api", "web-01", _ => 1000);

        var container = Assert.Single(await new MetricHealth(_store).ListContainersAsync(_now, default));

        Assert.Equal(
            ("shop-api", "web-01", "production"),
            (container.Container, container.Host, container.Environment));
        Assert.Equal(3d, container.Restarts);
        Assert.Equal(
            (0.93, 930d, 1000d),
            (container.MemoryRatio, container.MemoryUsedBytes, container.MemoryLimitBytes));
    }

    [Fact]
    public async Task ListContainersAsync_RecreatedContainer_CountsOnlyRestartsSinceItWasCreated()
    {
        // The container was removed and created again at 12:21: its count starts over at 0 and is 1 from 12:28.
        Add("container.restarts", "shop-api", "web-01", minute => minute < 21 ? 7 : minute < 28 ? 0 : 1, counter: true);

        var container = Assert.Single(await new MetricHealth(_store).ListContainersAsync(_now, default));

        Assert.Equal(1d, container.Restarts);
        Assert.Null(container.MemoryRatio);
    }

    [Fact]
    public async Task ListContainersAsync_OnlyMemoryPercent_UsesItAndHasNoRestartCount()
    {
        Add("container.memory.percent", "shop-api", "web-01", _ => 42.5);

        var container = Assert.Single(await new MetricHealth(_store).ListContainersAsync(_now, default));

        Assert.Equal(0.425, container.MemoryRatio!.Value, 6);
        Assert.Null(container.Restarts);
    }

    [Fact]
    public async Task ListContainersAsync_BetweenRestarts_HasNoMemoryShare()
    {
        // Caught between restarts, Docker reports 0 for usage and limit alike, and its percent as 0.
        Add("container.memory.usage.total", "shop-api", "web-01", _ => 0, sum: true);
        Add("container.memory.usage.limit", "shop-api", "web-01", _ => 0, sum: true);
        Add("container.memory.percent", "shop-api", "web-01", _ => 0);

        var container = Assert.Single(await new MetricHealth(_store).ListContainersAsync(_now, default));

        Assert.Equal(
            ((double?)null, (double?)null, (double?)null),
            (container.MemoryRatio, container.MemoryUsedBytes, container.MemoryLimitBytes));
    }

    [Fact]
    public async Task ListContainersAsync_MemoryAsSums_ReadsTheirLevels()
    {
        // docker_stats sends usage and limit as non-monotonic cumulative sums, not gauges.
        Add("container.memory.usage.total", "shop-api", "web-01", _ => 768, sum: true);
        Add("container.memory.usage.limit", "shop-api", "web-01", _ => 1024, sum: true);

        var container = Assert.Single(await new MetricHealth(_store).ListContainersAsync(_now, default));

        Assert.Equal(0.75, container.MemoryRatio);
    }

    [Fact]
    public async Task ListContainersAsync_HostMetricsOnly_IsEmpty()
    {
        var host = new TelemetryBlock(AlertBlockKind.Host, "web-01");
        Add("container.restarts", "shop-api", "web-01", _ => 1, block: host);

        Assert.Empty(await new MetricHealth(_store).ListContainersAsync(_now, default));
    }

    [Fact]
    public async Task HostAsync_HostWithContainers_ListsItsOwnContainersWithTheirFigures()
    {
        AddHostMemory("web-01");
        AddHostMemory("web-02");
        Add("container.cpu.utilization", "shop-api", "web-01", _ => 12.5);
        Add("container.memory.usage.total", "shop-api", "web-01", _ => 512e6);
        Add("container.memory.usage.limit", "shop-api", "web-01", _ => 1024e6);
        Add("container.restarts", "shop-api", "web-01", minute => minute < 10 ? 2 : 3, counter: true);
        Add("container.uptime", "shop-api", "web-01", minute => (minute - 10) * 60.0);
        Add("container.cpu.utilization", "db-proxy", "web-01", _ => 1);
        Add("container.cpu.utilization", "elsewhere", "web-02", _ => 50);

        var view = (await Builder().HostAsync("web-01", "1h", CancellationToken.None))!;

        Assert.Equal(["db-proxy", "shop-api"], view.Containers.Select(container => container.Name));
        var shop = view.Containers[1];
        Assert.Equal(
            ("web-01", "production", "shop/api:1.4", "4f2c9a1b7e3d"),
            (shop.Host, shop.Environment, shop.Image, shop.Id));
        Assert.Equal(
            (12.5, 512e6, 1024e6, 0.5),
            (shop.CpuPercent, shop.MemoryBytes, shop.MemoryLimitBytes, shop.MemoryRatio));
        Assert.Equal((1d, 3d, 1200d), (shop.Restarts, shop.RestartsTotal, shop.UptimeSeconds));
        Assert.Equal(12.5, shop.Cpu.Latest);
        Assert.Equal(60, shop.Cpu.Points.Count);
    }

    [Fact]
    public async Task OverviewAsync_Containers_HasACardPerContainerAndHost()
    {
        Add("container.cpu.utilization", "shop-api", "web-01", _ => 12.5);
        Add("container.cpu.utilization", "shop-api", "web-02", _ => 30);
        Add("container.memory.percent", "worker", "web-01", _ => 20);

        var overview = await Builder().OverviewAsync("1h", CancellationToken.None);

        Assert.Equal(
            [("shop-api", "web-01", 12.5), ("shop-api", "web-02", 30d), ("worker", "web-01", (double?)null)],
            overview.Containers.Select(container => (container.Name, container.Host, container.CpuPercent)));
        Assert.Equal(0.2, overview.Containers[2].MemoryRatio!.Value, 6);
        Assert.Empty(overview.Hosts);
    }

    private MetricViewBuilder Builder() => new(_store, new FakeTimeProvider(_now));

    private void Add(
        string name,
        string container,
        string host,
        Func<int, double> valueAtMinute,
        bool counter = false,
        TelemetryBlock? block = null,
        bool sum = false)
    {
        // A counter is a monotonic sum, such as the restart count; a sum that can fall is one such as memory usage.
        var series = _store.Add(new MetricSeries(
            0,
            name,
            counter || sum ? MetricKind.Sum : MetricKind.Gauge,
            "1",
            string.Empty,
            counter || sum ? MetricTemporality.Cumulative : MetricTemporality.Unspecified,
            counter,
            null,
            "production",
            host,
            new Dictionary<string, string>
            {
                ["container.name"] = container,
                ["container.id"] = "4f2c9a1b7e3d0c5a9e8f7d6c5b4a39281706f5e4d3c2b1a0f9e8d7c6b5a49382",
                ["container.image.name"] = "shop/api:1.4",
                ["host.name"] = host,
            },
            new Dictionary<string, string>(),
            _now.AddDays(-1),
            _now,
            block ?? new TelemetryBlock(AlertBlockKind.Container, container)));

        for (var minute = 0; minute <= 30; minute++)
        {
            _store.Point(series, new DateTimeOffset(2026, 11, 10, 12, minute, 0, TimeSpan.Zero), valueAtMinute(minute));
        }
    }

    private void AddHostMemory(string host)
    {
        var used = MetricTestData.HostSeries("system.memory.usage", MetricKind.Sum, false, _now, ("state", "used"));
        var series = _store.Add(used with
        {
            HostName = host,
            Block = new TelemetryBlock(AlertBlockKind.Host, host),
        });

        for (var minute = 0; minute <= 30; minute++)
        {
            _store.Point(series, new DateTimeOffset(2026, 11, 10, 12, minute, 0, TimeSpan.Zero), 4e9);
        }
    }
}
