using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Metrics;
using Veyra.Query.Metrics;
using static Veyra.Query.Tests.Metrics.MetricTestData;

namespace Veyra.Query.Tests.Metrics;

public class MetricViewBuilderTests
{
    // 12:04:20 UTC: the last five one-minute buckets are 12:00 to 12:04, with 11:59 as their baseline.
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 12, 4, 20, TimeSpan.Zero);
    private static readonly DateTimeOffset _baseline = new(2026, 11, 10, 11, 59, 0, TimeSpan.Zero);

    private readonly FakeMetricSearch _store = new();

    [Fact]
    public async Task OverviewAsync_HostCard_HasCpuShareMemoryShareLoadAndOs()
    {
        AddCpu("user", perSecond: 0.3);
        AddCpu("system", perSecond: 0.1);
        AddCpu("idle", perSecond: 0.6);
        AddLevel("system.memory.usage", 6e9, ("state", "used"));
        AddLevel("system.memory.usage", 2e9, ("state", "free"));
        AddLevel("system.cpu.load_average.1m", 1.6);

        var host = Assert.Single((await Builder().OverviewAsync("1h", CancellationToken.None)).Hosts);

        Assert.Equal(("web-01", "Ubuntu 24.04", 1), (host.Name, host.Os, host.Cpus));
        Assert.Equal(0.4, host.CpuRatio!.Value, 6);
        Assert.Equal(0.75, host.MemoryRatio!.Value, 6);
        Assert.Equal((1.6, 8e9), (host.Load, host.MemoryBytes));
        Assert.Equal(60, host.Cpu.Points.Count);
        Assert.Equal(0.4, host.Cpu.Latest!.Value, 6);
    }

    [Fact]
    public async Task OverviewAsync_SeveralCpus_CpuShareIsOfAllCpus()
    {
        AddCpu("user", perSecond: 0.2, cpu: "0");
        AddCpu("idle", perSecond: 0.8, cpu: "0");
        AddCpu("user", perSecond: 0.1, cpu: "1");
        AddCpu("idle", perSecond: 0.9, cpu: "1");

        var host = Assert.Single((await Builder().OverviewAsync("1h", CancellationToken.None)).Hosts);

        Assert.Equal(2, host.Cpus);
        Assert.Equal(0.15, host.CpuRatio!.Value, 6);
    }

    [Theory]
    [InlineData("Ubuntu 24.04.1 LTS (Noble Numbat)", "Ubuntu 24.04.1 LTS")]
    [InlineData("Linux 1eb2685c2886 6.18.33.2-microsoft-standard-WSL2 #1 SMP PREEMPT_DYNAMIC x86_64", "Linux")]
    public async Task OverviewAsync_LongOsDescription_ShowsAShortName(string description, string expected)
    {
        var memory = _store.Add(
            HostSeries("system.memory.usage", MetricKind.Sum, false, _now, ("state", "used")) with
            {
                ResourceAttributes = new Dictionary<string, string>
                {
                    ["host.name"] = "web-01",
                    ["os.description"] = description,
                    ["os.type"] = "linux",
                },
            });
        _store.Point(memory, _now.AddMinutes(-1), 6e9);

        var host = Assert.Single((await Builder().OverviewAsync("1h", CancellationToken.None)).Hosts);

        Assert.Equal(expected, host.Os);
    }

    [Fact]
    public async Task OverviewAsync_PointNewerThanLastSeen_CardShowsThePoint()
    {
        // Storage refreshes a series' last_seen only once a minute; the newest point is more precise.
        var minute = new DateTimeOffset(2026, 11, 10, 12, 3, 0, TimeSpan.Zero);
        var memory = _store.Add(HostSeries("system.memory.usage", MetricKind.Sum, false, minute, ("state", "used")));
        _store.Point(memory, minute, 6e9);

        var host = Assert.Single((await Builder().OverviewAsync("1h", CancellationToken.None)).Hosts);

        Assert.Equal(minute.AddSeconds(30), host.LastSeen);
    }

    [Fact]
    public async Task OverviewAsync_ReadOnlyFilesystem_IsNotADisk()
    {
        AddLevel("system.filesystem.usage", 40e9, ("mountpoint", "/"), ("type", "ext4"), ("mode", "rw"), ("state", "used"));
        AddLevel("system.filesystem.usage", 60e9, ("mountpoint", "/"), ("type", "ext4"), ("mode", "rw"), ("state", "free"));
        AddLevel("system.filesystem.usage", 8e8, ("mountpoint", "/cdrom"), ("type", "iso9660"), ("mode", "ro"), ("state", "used"));
        AddLevel("system.filesystem.usage", 0, ("mountpoint", "/cdrom"), ("type", "iso9660"), ("mode", "ro"), ("state", "free"));

        var host = Assert.Single((await Builder().OverviewAsync("1h", CancellationToken.None)).Hosts);

        Assert.Equal(("/", 0.4), (host.FullestDisk!.Mountpoint, host.FullestDisk.Ratio));
    }

    [Fact]
    public async Task OverviewAsync_ServiceCard_HasRequestRateP95AndServerErrorShare()
    {
        var ok = _store.Add(Durations("orders", 200, _now));
        var failing = _store.Add(Durations("orders", 503, _now));
        Grow(_store, ok, _baseline, 6, [57, 0, 0]);
        Grow(_store, failing, _baseline, 6, [0, 3, 0]);

        var service = Assert.Single((await Builder().OverviewAsync("1h", CancellationToken.None)).Services);

        Assert.Equal(("orders", "production", ".NET", 1), (service.Name, service.Environment, service.Runtime, service.Instances));
        Assert.Equal(1, service.RequestsPerSecond!.Value, 6);
        Assert.Equal(0.05, service.ServerErrorRatio!.Value, 6);
        Assert.Equal(0.1 * (57 / 57.0), service.P95Seconds!.Value, 6);
    }

    [Fact]
    public async Task ServiceAsync_SplitsRequestsByClassAndRanksRoutes()
    {
        var orders = _store.Add(Durations("orders", 200, _now));
        var pay = _store.Add(Durations("orders", 500, _now, route: "/pay"));
        Grow(_store, orders, _baseline, 6, [120, 0, 0]);
        Grow(_store, pay, _baseline, 6, [0, 60, 0]);

        var view = (await Builder().ServiceAsync("orders", "production", "1h", CancellationToken.None))!;

        Assert.Equal(2, view.Requests.Single(line => line.Label == "ok").Latest!.Value, 6);
        Assert.Equal(1, view.Requests.Single(line => line.Label == "5xx").Latest!.Value, 6);
        Assert.Equal(["/orders", "/pay"], view.Routes.Select(route => route.Route));
        Assert.Equal(1, view.Routes[1].ServerErrorRatio);
        Assert.Equal(["orders-1"], view.InstanceNames);
    }

    [Fact]
    public async Task ServiceAsync_GeneratedInstanceId_IsShortened()
    {
        // The .NET SDK makes up a GUID per process when the instance ID is not configured.
        var durations = Durations("orders", 200, _now) with
        {
            ResourceAttributes = new Dictionary<string, string>
            {
                ["service.name"] = "orders",
                ["service.instance.id"] = "8a548d03-a432-424b-9c09-8178eb67b402",
                ["telemetry.sdk.language"] = "dotnet",
            },
        };
        Grow(_store, _store.Add(durations), _baseline, 6, [10, 0, 0]);

        var view = (await Builder().ServiceAsync("orders", "production", "1h", CancellationToken.None))!;

        Assert.Equal(["8a548d03"], view.InstanceNames);
    }

    [Fact]
    public async Task HostAsync_DeviceMountedTwice_IsOneDisk()
    {
        AddLevel("system.filesystem.usage", 40e9, ("device", "/dev/sda1"), ("mountpoint", "/"), ("type", "ext4"), ("state", "used"));
        AddLevel("system.filesystem.usage", 60e9, ("device", "/dev/sda1"), ("mountpoint", "/"), ("type", "ext4"), ("state", "free"));
        AddLevel("system.filesystem.usage", 40e9, ("device", "/dev/sda1"), ("mountpoint", "/srv/backup"), ("type", "ext4"), ("state", "used"));
        AddLevel("system.filesystem.usage", 60e9, ("device", "/dev/sda1"), ("mountpoint", "/srv/backup"), ("type", "ext4"), ("state", "free"));
        AddLevel("system.filesystem.usage", 1e9, ("device", "/dev/sdb1"), ("mountpoint", "/data"), ("type", "ext4"), ("state", "used"));
        AddLevel("system.filesystem.usage", 9e9, ("device", "/dev/sdb1"), ("mountpoint", "/data"), ("type", "ext4"), ("state", "free"));

        var view = (await Builder().HostAsync("web-01", "1h", CancellationToken.None))!;

        Assert.Equal(["/", "/data"], view.Disks.Select(disk => disk.Mountpoint));
    }

    [Fact]
    public async Task ServiceAsync_UnknownService_IsNull()
    {
        Assert.Null(await Builder().ServiceAsync("nobody", null, "1h", CancellationToken.None));
    }

    [Fact]
    public async Task ChartAsync_CounterSplitByAttribute_ShowsRatesPerValue()
    {
        AddCpu("user", perSecond: 0.3);
        AddCpu("idle", perSecond: 0.6);

        var chart = (await Builder().ChartAsync("system.cpu.time", null, "state", "1h", null, null, default))!;

        Assert.Equal(("rate", "By/s"), (chart.Show, chart.Unit));
        Assert.Equal(["idle", "user"], chart.Lines.Select(line => line.Label));
        Assert.Equal(0.6, chart.Lines[0].Latest!.Value, 6);
        Assert.Contains("state", chart.SplitKeys);
    }

    [Fact]
    public async Task ChartAsync_Histogram_DefaultsToP95()
    {
        var series = _store.Add(Durations("orders", 200, _now));
        Grow(_store, series, _baseline, 6, [50, 48, 2]);

        var chart = (await Builder().ChartAsync("http.server.request.duration", null, null, "1h", null, null, default))!;

        Assert.Equal("p95", chart.Show);
        Assert.Equal(0.94375, Assert.Single(chart.Lines).Latest!.Value, 6);
    }

    private MetricViewBuilder Builder() => new(_store, new FakeTimeProvider(_now));

    private void AddCpu(string state, double perSecond, string cpu = "0")
    {
        var series = _store.Add(HostSeries("system.cpu.time", MetricKind.Sum, true, _now, ("cpu", cpu), ("state", state)));

        for (var minute = 0; minute <= 5; minute++)
        {
            _store.Point(series, _baseline.AddMinutes(minute), perSecond * 60 * minute);
        }
    }

    private void AddLevel(string name, double value, params (string Key, string Value)[] attributes)
    {
        var series = _store.Add(HostSeries(name, name.Contains("load", StringComparison.Ordinal) ? MetricKind.Gauge : MetricKind.Sum, false, _now, attributes));

        for (var minute = 0; minute <= 5; minute++)
        {
            _store.Point(series, _baseline.AddMinutes(minute), value);
        }
    }
}
