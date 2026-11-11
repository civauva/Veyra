using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Home;
using Veyra.Query.Tests.Metrics;

namespace Veyra.Query.Tests.Home;

public class HomeSnapshotBuilderTests
{
    // 07:34:12 UTC, so the current hour starts at 07:00.
    private static readonly DateTimeOffset _now = new(2026, 11, 7, 7, 34, 12, TimeSpan.Zero);
    private static readonly DateTimeOffset _hour = new(2026, 11, 7, 7, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _fiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _oneHour = TimeSpan.FromHours(1);

    private readonly FakeLogRates _rates = new();
    private readonly FakeLogSearch _search = new();
    private readonly FakeAlertStates _alerts = new();
    private readonly FakeLogStorageUsage _storage = new();
    private readonly FakeMetricSearch _metrics = new();

    [Fact]
    public async Task BuildAsync_WithoutMetrics_HasNoLatencyAndNoDisks()
    {
        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Null(home.Metrics.Latency);
        Assert.Empty(home.Disks);
    }

    [Fact]
    public async Task BuildAsync_RequestDurations_GiveTheLatencyTileWithTheSlowestService()
    {
        var minute = new DateTimeOffset(2026, 11, 7, 7, 29, 0, TimeSpan.Zero);
        var fast = _metrics.Add(MetricTestData.Durations("orders", 200, _now));
        var slow = _metrics.Add(MetricTestData.Durations("payments", 200, _now));
        MetricTestData.Grow(_metrics, fast, minute, 6, [100, 0, 0]);
        MetricTestData.Grow(_metrics, slow, minute, 6, [50, 48, 2]);

        var latency = (await Builder().BuildAsync("production", CancellationToken.None)).Metrics.Latency!;

        Assert.Equal(("payments", 2), (latency.SlowestService, latency.Services));
        Assert.Equal(0.94375, latency.SlowestP95Seconds, 6);
        Assert.InRange(latency.P95Seconds, 0.1, latency.SlowestP95Seconds);
    }

    [Fact]
    public async Task BuildAsync_FillingDisk_IsInTheOutlookWithItsDays()
    {
        var used = _metrics.Add(MetricTestData.HostSeries(
            "system.filesystem.usage",
            MetricKind.Sum,
            false,
            _now,
            ("mountpoint", "/"),
            ("state", "used"),
            ("type", "ext4")));
        var free = _metrics.Add(MetricTestData.HostSeries(
            "system.filesystem.usage",
            MetricKind.Sum,
            false,
            _now,
            ("mountpoint", "/"),
            ("state", "free"),
            ("type", "ext4")));

        // 100 GB, 80 % used now, growing by 1 % of the disk a day.
        for (var hour = 0; hour <= 24; hour++)
        {
            var usedBytes = 80e9 - (hour * 1e9 / 24);
            _metrics.Point(used, _hour.AddHours(-hour), usedBytes);
            _metrics.Point(free, _hour.AddHours(-hour), 100e9 - usedBytes);
        }

        for (var minute = 29; minute <= 34; minute++)
        {
            _metrics.Point(used, _hour.AddMinutes(minute), 80e9);
            _metrics.Point(free, _hour.AddMinutes(minute), 20e9);
        }

        var disk = Assert.Single((await Builder().BuildAsync("production", CancellationToken.None)).Disks);

        Assert.Equal(("web-01", "/", 0.8), (disk.Host, disk.Mountpoint, disk.Ratio));
        Assert.InRange(disk.DaysUntilFull!.Value, 19.5, 20.5);
    }

    [Fact]
    public async Task BuildAsync_NoAlerts_IsClearWithErrorRatioAndIngestRate()
    {
        _rates.ByWindow[_fiveMinutes] =
        [
            new ServiceLogRate("production", "orders", 600, 3),
            new ServiceLogRate("production", "payments", 300, 0),
        ];

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Equal(_now, home.At);
        Assert.Null(home.Environment);
        Assert.Equal(HomeConditionKind.Clear, home.Condition.Kind);
        Assert.Null(home.Condition.Over);
        Assert.Equal(3.0 / 900, home.Condition.ErrorRatio);
        Assert.Equal(3.0 / 900, home.Metrics.ErrorRatio);
        Assert.Equal(3, home.Metrics.IngestPerSecond);
        Assert.Equal(300, home.Metrics.WindowSeconds);
        Assert.Empty(home.Alerts);
        Assert.Equal(new HealthCountsResponse(0, 0, 2, 0), home.Counts);
    }

    [Fact]
    public async Task BuildAsync_NoRecordsAtAll_HasNoErrorRatio()
    {
        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Null(home.Condition.ErrorRatio);
        Assert.Null(home.Metrics.ErrorRatio);
        Assert.Equal(0, home.Metrics.IngestPerSecond);
        Assert.Empty(home.Blocks);
    }

    [Fact]
    public async Task BuildAsync_AnyWarning_IsStormOverServicesWithWarningsNumberedFirst()
    {
        _rates.ByWindow[_fiveMinutes] = [Rate("billing-worker"), Rate("payments-gateway"), Rate("orders")];
        _alerts.Alerts.Add(Alert("billing-worker", AlertSeverity.Caution, _now.AddHours(-2)));
        _alerts.Alerts.Add(Alert("payments-gateway", AlertSeverity.Warning, _now.AddMinutes(-36)));

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Equal(HomeConditionKind.Storm, home.Condition.Kind);
        Assert.Equal("Services", home.Condition.Over);
        Assert.Equal(["payments-gateway", "billing-worker"], home.Alerts.Select(alert => alert.Service));
        Assert.Equal([1, 2], home.Alerts.Select(alert => alert.N));
        Assert.Equal("service:production/payments-gateway", home.Alerts[0].Block);
        Assert.Equal(_now.AddMinutes(-36), home.Alerts[0].Since);
        Assert.Equal(new HealthCountsResponse(1, 1, 1, 0), home.Counts);
    }

    [Fact]
    public async Task BuildAsync_CautionsOnly_IsShowers()
    {
        _rates.ByWindow[_fiveMinutes] = [Rate("orders")];
        _alerts.Alerts.Add(Alert("orders", AlertSeverity.Caution, _now.AddMinutes(-10)));

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Equal(HomeConditionKind.Showers, home.Condition.Kind);
    }

    [Fact]
    public async Task BuildAsync_SameSeverity_OlderAlertComesFirst()
    {
        _alerts.Alerts.Add(Alert("newer", AlertSeverity.Warning, _now.AddMinutes(-5)));
        _alerts.Alerts.Add(Alert("older", AlertSeverity.Warning, _now.AddMinutes(-50)));

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Equal(["older", "newer"], home.Alerts.Select(alert => alert.Service));
    }

    [Fact]
    public async Task BuildAsync_BlockHealth_ComesOnlyFromAlerts()
    {
        // Half of the records are errors, but no rule has raised an alert yet, so the block is not lit (ADR 0007).
        _rates.ByWindow[_fiveMinutes] = [new ServiceLogRate("production", "orders", 100, 50), Rate("payments")];
        var alert = Alert("payments", AlertSeverity.Warning, _now.AddMinutes(-3));
        _alerts.Alerts.Add(alert);

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        var orders = home.Blocks.Single(block => block.Name == "orders");
        var payments = home.Blocks.Single(block => block.Name == "payments");
        Assert.Equal(BlockHealth.Ok, orders.Health);
        Assert.Equal(0.5, orders.ErrorRatio);
        Assert.Equal(100, orders.Records);
        Assert.Equal(50, orders.Errors);
        Assert.Equal(BlockHealth.Warning, payments.Health);
        Assert.Equal([alert.Id], payments.AlertIds);
        Assert.Equal("service", payments.Kind);
        Assert.Equal("service:production/payments", payments.Id);
    }

    [Fact]
    public async Task BuildAsync_HostAlerts_LightOneHostBlockAfterTheServices()
    {
        _rates.ByWindow[_fiveMinutes] = [Rate("web-01")];
        var root = Alert("web-01", AlertSeverity.Caution, _now.AddHours(-4)) with
        {
            Rule = "disk-filling",
            Subject = AlertSubject.Host("web-01", "/"),
        };
        var logs = root with { Id = Guid.CreateVersion7(), Rule = "disk-space", Subject = AlertSubject.Host("web-01", "/var/log") };
        _alerts.Alerts.Add(root);
        _alerts.Alerts.Add(logs);

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Equal([("service", "web-01"), ("host", "web-01")], home.Blocks.Select(block => (block.Kind, block.Name)));
        var host = home.Blocks[1];
        Assert.Equal("host:production/web-01", host.Id);
        Assert.Equal(BlockHealth.Caution, host.Health);
        Assert.Equal(new[] { root.Id, logs.Id }.Order(), host.AlertIds.Order());
        Assert.Equal(BlockHealth.Ok, home.Blocks[0].Health);
        Assert.Equal(1, home.Counts.Caution);

        var alert = home.Alerts[0];
        Assert.Equal(("host", "web-01", "/", (string?)null), (alert.Kind, alert.Name, alert.Part, alert.Service));
        Assert.Equal("host:production/web-01", alert.Block);
        Assert.Equal("Hosts", home.Condition.Over);
    }

    [Fact]
    public async Task BuildAsync_ServiceWarningAndHostCaution_IsAStormOverServices()
    {
        _alerts.Alerts.Add(Alert("payments", AlertSeverity.Warning, _now.AddMinutes(-36)));
        _alerts.Alerts.Add(Alert("web-01", AlertSeverity.Caution, _now.AddHours(-4)) with
        {
            Rule = "disk-filling",
            Subject = AlertSubject.Host("web-01", "/"),
        });

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Equal(HomeConditionKind.Storm, home.Condition.Kind);
        Assert.Equal("Services", home.Condition.Over);
        Assert.Equal(["payments", "web-01"], home.Alerts.Select(alert => alert.Name));
    }

    [Fact]
    public async Task BuildAsync_Blocks_IncludeServicesQuietNowAndSilentServicesWithAlerts()
    {
        _rates.ByWindow[_oneHour] = [Rate("quiet-now")];
        _alerts.Alerts.Add(Alert("silent", AlertSeverity.Caution, _now.AddHours(-3)));

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Equal(["quiet-now", "silent"], home.Blocks.Select(block => block.Name));
        Assert.Null(home.Blocks[0].ErrorRatio);
        Assert.Equal(0, home.Blocks[0].Records);
    }

    [Fact]
    public async Task BuildAsync_Blocks_AreOrderedByNameSoPositionsStayStable()
    {
        _rates.ByWindow[_fiveMinutes] = [Rate("zeta"), Rate("alpha"), Rate("mid")];

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Equal(["alpha", "mid", "zeta"], home.Blocks.Select(block => block.Name));
    }

    [Fact]
    public async Task BuildAsync_Environment_FiltersEverythingButListsEveryEnvironment()
    {
        _rates.ByWindow[_fiveMinutes] =
        [
            new ServiceLogRate("production", "orders", 100, 10),
            new ServiceLogRate("staging", "orders", 100, 1),
            new ServiceLogRate(null, "legacy", 100, 0),
        ];
        _alerts.Alerts.Add(Alert("orders", AlertSeverity.Warning, _now.AddMinutes(-3)));
        _alerts.Alerts.Add(Alert("orders", AlertSeverity.Caution, _now.AddMinutes(-3)) with { Environment = "staging" });

        var home = await Builder().BuildAsync("staging", CancellationToken.None);

        Assert.Equal("staging", home.Environment);
        Assert.Equal(HomeConditionKind.Showers, home.Condition.Kind);
        Assert.Equal(0.01, home.Condition.ErrorRatio);
        Assert.Equal(["staging"], home.Alerts.Select(alert => alert.Environment));
        Assert.Equal(["staging"], home.Blocks.Select(block => block.Environment));
        Assert.Equal(
            [
                new HomeEnvironmentResponse("production", HomeConditionKind.Storm, 0.1),
                new HomeEnvironmentResponse("staging", HomeConditionKind.Showers, 0.01),
            ],
            home.Environments);
        Assert.Equal(["staging"], _search.Summaries[0].Query.Environments);
    }

    [Fact]
    public async Task BuildAsync_Resolved_ListsTheLastDayNewestFirstAndNotAsOpen()
    {
        _alerts.Alerts.Add(Alert("early", AlertSeverity.Warning, _now.AddHours(-6)) with { ResolvedAt = _now.AddHours(-5) });
        _alerts.Alerts.Add(Alert("late", AlertSeverity.Caution, _now.AddHours(-2)) with { ResolvedAt = _now.AddHours(-1) });

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Empty(home.Alerts);
        Assert.Equal(["late", "early"], home.Resolved.Select(alert => alert.Service));
        Assert.All(home.Resolved, alert => Assert.Null(alert.N));
        Assert.Equal(_now.AddHours(-24), _alerts.LastResolvedSince);
        Assert.Equal(new HealthCountsResponse(0, 0, 0, 0), home.Counts);
    }

    [Fact]
    public async Task BuildAsync_Hourly_ObservesTheLastDayAndThisHourByTheHour()
    {
        _search.Hours[_hour.AddHours(-1)] = (1000, 4);
        _search.Hours[_hour] = (500, 29);

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        var (query, bucketSize) = _search.Summaries[0];
        Assert.Equal(_hour.AddHours(-23), query.From);
        Assert.Equal(_now, query.To);
        Assert.Equal(_oneHour, bucketSize);
        Assert.Equal(24, home.Hourly.Observed.Count);
        Assert.Equal(new LogBucket(_hour, 500, 29), home.Hourly.Observed[^1]);
    }

    [Fact]
    public async Task BuildAsync_Hourly_ExpectsTheNextSixHoursFromHistory()
    {
        // Three days of history: every hour had 100 records, except 10:00, which had 200.
        for (var start = _hour.AddDays(-3); start < _hour; start += _oneHour)
        {
            _search.Hours[start] = (start.Hour == 10 ? 200 : 100, 0);
        }

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        var history = _search.Summaries[1];
        Assert.Equal(_hour.AddDays(-7), history.Query.From);
        Assert.Equal(_hour, history.Query.To);
        Assert.Equal(6, home.Hourly.Expected.Count);
        Assert.Equal(_hour.AddHours(1), home.Hourly.Expected[0].Start);
        var average = 7500 / 72.0;
        Assert.Equal(100 / average, home.Hourly.Expected[0].Load!.Value, 6);
        Assert.Equal(200 / average, home.Hourly.Expected.Single(hour => hour.Start.Hour == 10).Load!.Value, 6);
        Assert.Equal(3, home.Hourly.DaysOfHistory);
    }

    [Fact]
    public async Task BuildAsync_Hourly_LessThanTwoDaysOfAnHour_IsStillLearning()
    {
        for (var start = _hour.AddHours(-30); start < _hour; start += _oneHour)
        {
            _search.Hours[start] = (100, 0);
        }

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        // The next six hours, 08:00 to 13:00, were each seen only once, yesterday, so none of them is known yet.
        Assert.All(home.Hourly.Expected, hour => Assert.Null(hour.Load));
        Assert.Equal(2, home.Hourly.DaysOfHistory);
    }

    [Fact]
    public async Task BuildAsync_Hourly_NoHistory_HasNoDaysAndNoExpectation()
    {
        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.All(home.Hourly.Expected, hour => Assert.Null(hour.Load));
        Assert.Equal(0, home.Hourly.DaysOfHistory);
    }

    [Fact]
    public async Task BuildAsync_Hourly_HistoryIsReadOncePerHourAndEnvironment()
    {
        var builder = Builder();

        await builder.BuildAsync(null, CancellationToken.None);
        await builder.BuildAsync(null, CancellationToken.None);
        await builder.BuildAsync("staging", CancellationToken.None);

        // Each build reads the observed hours; the seven-day history only once for each environment.
        Assert.Equal(5, _search.Summaries.Count);
    }

    [Fact]
    public async Task BuildAsync_Storage_ComesFromUsageWithItsOutlook()
    {
        const long gigabyte = 1024L * 1024 * 1024;
        _storage.Usage = new LogStorageUsage(gigabyte, 20 * gigabyte, 30, gigabyte);

        var home = await Builder().BuildAsync(null, CancellationToken.None);

        Assert.Equal(
            new LogStorageResponse(gigabyte, 20 * gigabyte, gigabyte, 30, 19, true),
            home.Storage);
    }

    private HomeSnapshotBuilder Builder() =>
        new(_rates, _search, _alerts, _storage, _metrics, new FakeTimeProvider(_now));

    private static ServiceLogRate Rate(string service) => new("production", service, 100, 0);

    private static AlertState Alert(string service, AlertSeverity severity, DateTimeOffset openedAt) => new(
        Id: Guid.CreateVersion7(),
        Rule: "error-rate",
        Environment: "production",
        Subject: AlertSubject.Service(service),
        Severity: severity,
        Value: 0.1,
        Peak: 0.2,
        Threshold: severity == AlertSeverity.Warning ? 0.05 : 0.01,
        Window: TimeSpan.FromMinutes(5),
        OpenedAt: openedAt,
        UpdatedAt: _now,
        ResolvedAt: null);
}
