using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Home;
using Veyra.Query.Resources;
using Veyra.Query.Tests.Home;
using Veyra.Query.Tests.Metrics;
using static Veyra.Query.Tests.Metrics.MetricTestData;

namespace Veyra.Query.Tests.Resources;

public class HostListBuilderTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 26, 12, 4, 20, TimeSpan.Zero);
    private static readonly DateTimeOffset _first = new(2026, 11, 26, 11, 4, 0, TimeSpan.Zero);

    private readonly FakeMetricSearch _store = new();
    private readonly FakeAlertStates _alerts = new();

    public HostListBuilderTests()
    {
        // web-01 is busy a third of the time, with its disk 89 % full; sql-01 stopped two hours ago.
        Counter(Host("system.cpu.time", "web-01", ("cpu", "0"), ("state", "user")), 0.3);
        Counter(Host("system.cpu.time", "web-01", ("cpu", "0"), ("state", "idle")), 0.7);
        Level(Host("system.memory.usage", "web-01", ("state", "used")), 6e9);
        Level(Host("system.memory.usage", "web-01", ("state", "free")), 2e9);
        Level(Host("system.filesystem.usage", "web-01", ("mountpoint", "/"), ("state", "used")), 89e9);
        Level(Host("system.filesystem.usage", "web-01", ("mountpoint", "/"), ("state", "free")), 11e9);
        _store.Add(Host("system.memory.usage", "sql-01", ("state", "used")) with { LastSeen = _now.AddHours(-2) });

        // A container on web-01 using half a CPU, and what else runs there.
        Level(Container("shop-api", "web-01"), 50);
        _store.Add(Durations("orders", 200, _now) with
        {
            Block = new TelemetryBlock(AlertBlockKind.Service, "orders"),
        });
        _store.Add(Host("sqlserver.user.connection.count", "web-01") with
        {
            Block = TelemetryBlock.Database("microsoft.sql_server", "SQL-PROD-01"),
        });

        _alerts.Alerts.Add(new AlertState(
            Guid.NewGuid(),
            "disk-space",
            null,
            AlertSubject.Host("web-01", "/"),
            AlertSeverity.Caution,
            0.89,
            0.89,
            0.85,
            TimeSpan.FromMinutes(5),
            _now,
            _now,
            null));
    }

    [Fact]
    public async Task ReadAsync_Hosts_GiveTheirCpuMemoryAndFullestDiskPerBucket()
    {
        var listing = await Builder().ReadAsync(Query(), HostListBuilder.HostKind, null, default);

        var inside = listing.Window.Starts;
        Assert.All(inside, start => Assert.Equal(0.3, listing.Cpu[start], 6));
        Assert.All(inside, start => Assert.Equal(0.75, listing.Memory[start], 6));
        Assert.All(inside, start => Assert.Equal(0.89, listing.Disk[start], 6));
        Assert.Equal(["web-01", "sql-01"], listing.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task ListAsync_WorstFirst_CautionThenSilentThenHealthyWithTheirFigures()
    {
        var list = (await Builder().ListAsync(Query(), null, null, default))!;

        Assert.Equal(["web-01", "sql-01", "shop-api"], list.Items.Select(item => item.Name));
        var web = list.Items[0];
        Assert.Equal(("host", BlockHealth.Caution, false, "Ubuntu 24.04"), (web.Kind, web.Health, web.Silent, web.Os));
        Assert.Equal(0.3, web.CpuRatio!.Value, 6);
        Assert.Equal(0.75, web.MemoryRatio!.Value, 6);
        Assert.Equal(0.89, web.FullestDiskRatio!.Value, 6);
        Assert.Equal(["orders", "shop-api", "SQL-PROD-01"], web.Runs);
        Assert.Equal(3, web.RunsCount);
        var sql = list.Items[1];
        Assert.True(sql.Silent);
        Assert.Null(sql.CpuRatio);
        var container = list.Items[2];
        Assert.Equal(("container", "web-01", 0.5), (container.Kind, container.Parent, container.CpuRatio));
    }

    [Fact]
    public async Task ListAsync_Header_CountsKnownOnlineAndContainersOfTheWholeSet()
    {
        var list = (await Builder().ListAsync(Query(limit: 1), null, null, default))!;

        var header = list.Header;
        Assert.Equal((2, 1, 1), (header.Known, header.Online, header.Containers));
        Assert.Equal(0.4, header.CpuAverage!.Value, 6);
        Assert.Equal(("shop-api", 0.5), (header.CpuBusiest!.Name, header.CpuBusiest.Ratio));
        Assert.Equal("web-01", header.MemoryFullest!.Name);
        Assert.Equal(0, header.DisksFilling);
        Assert.Null(header.DisksFillingFirst);
        Assert.Equal(3, list.Total);
        Assert.NotNull(list.Next);
    }

    [Fact]
    public async Task ListAsync_KindStateAndSearch_FilterOnTheServer()
    {
        var containers = (await Builder().ListAsync(Query(), "container", null, default))!;
        var silent = (await Builder().ListAsync(Query(), null, "silent", default))!;
        var online = (await Builder().ListAsync(Query(), "host", "online", default))!;
        var found = (await Builder().ListAsync(Query(search: "SQL"), null, null, default))!;

        Assert.Equal(["shop-api"], containers.Items.Select(item => item.Name));
        Assert.Equal((0, 1), (containers.Header.Known, containers.Header.Containers));
        Assert.Equal(["sql-01"], silent.Items.Select(item => item.Name));
        Assert.Equal(["web-01"], online.Items.Select(item => item.Name));
        Assert.Equal(["sql-01"], found.Items.Select(item => item.Name));
    }

    private HostListBuilder Builder() => new(
        _store,
        _alerts,
        new FixedOptions<QueryOptions>(new QueryOptions()),
        new FakeTimeProvider(_now));

    private static ResourceListQuery Query(string? search = null, int limit = 50) =>
        new(null, "1h", ResourceHealthFilter.Parse(null)!, search, null, null, limit);

    private static MetricSeries Host(string name, string host, params (string Key, string Value)[] attributes)
    {
        var counter = name == "system.cpu.time";

        return HostSeries(name, counter ? MetricKind.Sum : MetricKind.Gauge, counter, _now, attributes) with
        {
            HostName = host,
            ResourceAttributes = new Dictionary<string, string>
            {
                ["host.name"] = host,
                ["os.description"] = "Ubuntu 24.04",
            },
            Block = new TelemetryBlock(AlertBlockKind.Host, host),
        };
    }

    private static MetricSeries Container(string name, string host) =>
        HostSeries("container.cpu.utilization", MetricKind.Gauge, false, _now) with
        {
            HostName = host,
            ResourceAttributes = new Dictionary<string, string> { ["container.name"] = name, ["host.name"] = host },
            Block = new TelemetryBlock(AlertBlockKind.Container, name),
        };

    private void Counter(MetricSeries series, double perSecond)
    {
        var stored = _store.Add(series);

        for (var minute = 0; minute <= 60; minute++)
        {
            _store.Point(stored, _first.AddMinutes(minute), perSecond * 60 * minute);
        }
    }

    private void Level(MetricSeries series, double value)
    {
        var stored = _store.Add(series);

        for (var minute = 0; minute <= 60; minute++)
        {
            _store.Point(stored, _first.AddMinutes(minute), value);
        }
    }
}
