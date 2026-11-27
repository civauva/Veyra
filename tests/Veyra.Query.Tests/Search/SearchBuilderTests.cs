using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Search;
using Veyra.Core.Telemetry;
using Veyra.Query.Home;
using Veyra.Query.Search;
using Veyra.Query.Tests.Home;
using Veyra.Query.Tests.Metrics;

namespace Veyra.Query.Tests.Search;

public class SearchBuilderTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSignalSearch _signals = new();
    private readonly FakeWordCounts _words = new();
    private readonly FakeAlertStates _alerts = new();
    private readonly FakeMetricHealth _health = new();
    private readonly FakeMetricSearch _metrics = new();

    [Fact]
    public async Task SearchAsync_Services_RanksExactThenPrefixThenContainsWithHealthAndFigures()
    {
        _signals.Blocks.AddRange(
        [
            new FoundBlock(AlertBlockKind.Service, "old-payments", null, 4_812),
            new FoundBlock(AlertBlockKind.Service, "Payments.API", null, 10),
            new FoundBlock(AlertBlockKind.Service, "pay", null, 0),
        ]);
        _alerts.Alerts.Add(Alert(AlertSubject.Service("Payments.API"), AlertSeverity.Caution));
        _health.Latencies.Add(new ServiceLatency("Payments.API", "production", 0.742, 120));

        var result = await Builder().SearchAsync("pay", null, _now.AddDays(-1), _now, CancellationToken.None);

        Assert.Equal(["pay", "Payments.API", "old-payments"], result.Services.Items.Select(item => item.Name));
        Assert.All(result.Services.Items, item => Assert.Equal("service", item.Kind));
        Assert.Equal(
            [BlockHealth.Ok, BlockHealth.Caution, BlockHealth.Ok],
            result.Services.Items.Select(item => item.Health));
        Assert.Equal([null, "742 ms p95", "4,812 logs"], result.Services.Items.Select(item => item.Detail));
        Assert.False(result.Services.Partial);
    }

    [Fact]
    public async Task SearchAsync_ManyMatches_ReturnsFivePerGroup()
    {
        _signals.Blocks.AddRange(Enumerable.Range(1, 8)
            .Select(index => new FoundBlock(AlertBlockKind.Service, $"pay-{index}", null, 0)));

        var result = await Builder().SearchAsync("pay", null, _now.AddDays(-1), _now, CancellationToken.None);

        Assert.Equal(5, result.Services.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_HostsAndContainers_SayHowFullAndHowMuchMemory()
    {
        _signals.Blocks.AddRange(
        [
            new FoundBlock(AlertBlockKind.Host, "pay-host", null, 0),
            new FoundBlock(AlertBlockKind.Container, "payments-5c2", null, 0),
        ]);
        _health.Disks.Add(new DiskHealth("pay-host", null, "/", 81, 100, 0.81, null));
        _health.Disks.Add(new DiskHealth("pay-host", null, "/data", 10, 100, 0.10, null));
        _health.Containers.Add(new ContainerHealth("payments-5c2", "pay-host", null, 0, 0.64, 64, 100));
        _alerts.Alerts.Add(Alert(AlertSubject.Host("pay-host", "/"), AlertSeverity.Warning));

        var result = await Builder().SearchAsync("pay", null, _now.AddDays(-1), _now, CancellationToken.None);

        var host = result.Hosts.Items[0];
        var container = result.Hosts.Items[1];
        Assert.Equal(
            ("host", "pay-host", BlockHealth.Warning, "disk 81%"),
            (host.Kind, host.Name, host.Health, host.Detail));
        Assert.Equal(("container", "memory 64%"), (container.Kind, container.Detail));
        Assert.Null(host.Engine);
    }

    [Fact]
    public async Task SearchAsync_Databases_NameServersAndDatabasesWithEngineAndSize()
    {
        var server = TelemetryBlock.Database("microsoft.sql_server", "SQL-PROD-01");
        _signals.Blocks.AddRange(
        [
            new FoundBlock(AlertBlockKind.Database, server.Name, "PaymentsDB", 0),
            new FoundBlock(AlertBlockKind.Database, "microsoft.sql_server PAYSQL-01", null, 0),
        ]);
        var size = _metrics.Add(new MetricSeries(
            0,
            "sqlserver.database.size",
            MetricKind.Gauge,
            "By",
            "",
            MetricTemporality.Unspecified,
            false,
            null,
            null,
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["database_name"] = "PaymentsDB" },
            _now.AddDays(-1),
            _now,
            server));
        _metrics.Point(size, _now.AddMinutes(-5), 22d * (1L << 30));

        var result = await Builder().SearchAsync("pay", null, _now.AddDays(-1), _now, CancellationToken.None);

        var database = result.Databases.Items[0];
        Assert.Equal(
            ("database", "PaymentsDB", "microsoft.sql_server", "SQL-PROD-01", "22 GB"),
            (database.Kind, database.Name, database.Engine, database.Server, database.Detail));
        var other = result.Databases.Items[1];
        Assert.Equal(("server", "PAYSQL-01", "PAYSQL-01"), (other.Kind, other.Name, other.Server));
    }

    [Fact]
    public async Task SearchAsync_SlowGroup_IsPartialWhileTheOthersAnswer()
    {
        _signals.Blocks.Add(new FoundBlock(AlertBlockKind.Service, "payments", null, 0));
        _signals.Slow.Add(AlertBlockKind.Host);

        var result = await Builder(TimeSpan.FromMilliseconds(50))
            .SearchAsync("pay", null, _now.AddDays(-1), _now, CancellationToken.None);

        Assert.True(result.Hosts.Partial);
        Assert.Empty(result.Hosts.Items);
        Assert.False(result.Services.Partial);
        Assert.Equal("payments", Assert.Single(result.Services.Items).Name);
    }

    [Fact]
    public async Task SearchAsync_Logs_CountsTheWordsPartByPartAndKeepsWhatItHasWhenOutOfTime()
    {
        _words.PerPart = 7;
        _words.SlowAfterFirst = true;

        var result = await Builder(TimeSpan.FromMilliseconds(50))
            .SearchAsync("timeout", "production", _now.AddDays(-1), _now, CancellationToken.None);

        Assert.Equal(7, result.Logs.Count);
        Assert.True(result.Logs.Partial);
        var counted = Assert.Single(_words.Counted);
        Assert.Equal(("timeout", "production"), (counted.Text, Assert.Single(counted.Environments!)));
        Assert.Equal((_now.AddHours(-1), _now), (counted.From, counted.To));
    }

    [Fact]
    public async Task SearchAsync_Logs_WholeWindowInTime_IsNotPartial()
    {
        _words.PerPart = 2;

        var result = await Builder().SearchAsync("timeout", null, _now.AddDays(-1), _now, CancellationToken.None);

        Assert.Equal(12, result.Logs.Count);
        Assert.False(result.Logs.Partial);
    }

    [Fact]
    public async Task SearchAsync_Traces_AddsUpEachOperationOverTheWindow()
    {
        _signals.Operations.Add(new FoundOperation("POST /payments", "Payments.API", 3, 1));
        _signals.Operations.Add(new FoundOperation("GET /orders", "orders", 3, 0));

        var result = await Builder().SearchAsync("pay", null, _now.AddDays(-1), _now, CancellationToken.None);

        var operation = Assert.Single(result.Traces.Items);
        Assert.Equal(
            ("operation", "POST /payments", "Payments.API", 18L, "18 in 24 h, 6 failed"),
            (operation.Kind, operation.Name, operation.Service, operation.Count, operation.Detail));
        Assert.Equal(_now, _signals.OperationSearches[0].To);
    }

    [Fact]
    public async Task SearchAsync_EmptyText_AnswersNothingAndReadsNothing()
    {
        _signals.Blocks.Add(new FoundBlock(AlertBlockKind.Service, "payments", null, 0));

        var result = await Builder().SearchAsync("  ", null, _now.AddDays(-1), _now, CancellationToken.None);

        Assert.Empty(result.Services.Items);
        Assert.Equal(0, result.Logs.Count);
        Assert.Empty(_words.Counted);
        Assert.Empty(_signals.OperationSearches);
    }

    private SearchBuilder Builder(TimeSpan? budget = null) => new(
        _signals,
        _words,
        _alerts,
        _health,
        _metrics,
        new SearchBudget(budget ?? TimeSpan.FromSeconds(10)),
        new FakeTimeProvider(_now),
        NullLogger<SearchBuilder>.Instance);

    private static AlertState Alert(AlertSubject subject, AlertSeverity severity) => new(
        Guid.NewGuid(),
        "error-rate",
        "production",
        subject,
        severity,
        0.2,
        0.2,
        0.1,
        TimeSpan.FromMinutes(5),
        _now.AddMinutes(-10),
        _now,
        null);
}
