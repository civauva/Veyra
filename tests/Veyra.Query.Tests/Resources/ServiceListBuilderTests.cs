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

public class ServiceListBuilderTests
{
    // 12:04:20: the last hour is 60 one-minute buckets from 11:05, the hour before it 60 more from 10:05.
    private static readonly DateTimeOffset _now = new(2026, 11, 26, 12, 4, 20, TimeSpan.Zero);
    private static readonly DateTimeOffset _baseline = new(2026, 11, 26, 10, 4, 0, TimeSpan.Zero);

    private readonly FakeMetricSearch _store = new();
    private readonly FakeAlertStates _alerts = new();

    public ServiceListBuilderTests()
    {
        // orders: 10 requests a minute answered 200 and 1 answered 500 for two hours; a new route adds 10 a minute in
        // the last hour only.
        Grow(_store, _store.Add(Service(Durations("orders", 200, _now))), _baseline, 121, [8, 2, 0]);
        Grow(_store, _store.Add(Service(Durations("orders", 500, _now))), _baseline, 121, [1, 0, 0]);
        var pay = _store.Add(Service(Durations("orders", 200, _now, "/pay")));
        Grow(_store, pay, _baseline.AddHours(1), 61, [10, 0, 0]);

        // billing stopped three hours ago; catalog sends runtime metrics only; search is in staging.
        _store.Add(Runtime("billing", "production", _now.AddHours(-3)));
        _store.Add(Runtime("catalog", "production", _now));
        _store.Add(Runtime("search", "staging", _now));

        _alerts.Alerts.Add(Alert(AlertSubject.Service("orders"), AlertSeverity.Caution));
    }

    [Fact]
    public async Task ReadAsync_Production_AddsUpRequestsAndServerErrorsPerBucket()
    {
        var listing = await Builder().ReadAsync(Query(), null, default);

        Assert.Equal(60, listing.Window.Starts.Count);
        Assert.All(listing.Window.Starts, start => Assert.Equal(21, listing.Requests[start], 6));
        Assert.All(listing.Window.Starts, start => Assert.Equal(1, listing.Errors[start], 6));
        Assert.All(listing.Requests.Keys, start => Assert.True(start >= listing.Window.Start));
    }

    [Fact]
    public async Task ListAsync_Production_HeaderCountsTheWholeSetAndComparesWithTheHourBefore()
    {
        var list = (await Builder().ListAsync(Query(), null, default))!;

        var header = list.Header;
        Assert.Equal((3, 2, 0, 1, 1), (header.Total, header.Healthy, header.Warning, header.Caution, header.Silent));
        Assert.Equal(1260, header.Requests);
        Assert.Equal((1260.0 / 660) - 1, header.RequestsChange!.Value, 6);
        Assert.Equal(60.0 / 1260, header.ErrorRatio!.Value, 6);
        Assert.Equal((60.0 / 1260) - (60.0 / 660), header.ErrorRatioChange!.Value, 6);
        Assert.NotNull(header.P95Seconds);
        Assert.Equal((3, 2), (header.Instances, header.Hosts));
    }

    [Fact]
    public async Task ListAsync_WorstFirst_CautionThenSilentThenHealthy()
    {
        var list = (await Builder().ListAsync(Query(), null, default))!;

        Assert.Equal(["orders", "billing", "catalog"], list.Items.Select(item => item.Name));
        var orders = list.Items[0];
        Assert.Equal((BlockHealth.Caution, false), (orders.Health, orders.Silent));
        Assert.Equal(21, orders.RequestsPerMinute!.Value, 6);
        Assert.Equal(60.0 / 1260, orders.ErrorRatio!.Value, 6);
        Assert.Equal(["web-01"], orders.Hosts);
        Assert.Equal(24, orders.Requests.Count);
        Assert.All(orders.Requests, slice => Assert.Equal(21, slice!.Value, 6));
        var billing = list.Items[1];
        Assert.True(billing.Silent);
        Assert.Null(billing.RequestsPerMinute);
        Assert.All(billing.Requests, slice => Assert.Null(slice));
        Assert.Equal(3, list.Total);
        Assert.Null(list.Next);
    }

    [Fact]
    public async Task ListAsync_HealthSearchHostAndSort_FilterOnTheServer()
    {
        var silent = (await Builder().ListAsync(Query(health: "silent"), null, default))!;
        var found = (await Builder().ListAsync(Query(search: "CAT"), null, default))!;
        var onHost = (await Builder().ListAsync(Query(), "web-01", default))!;
        var byName = (await Builder().ListAsync(Query(sort: "name"), null, default))!;

        Assert.Equal(["billing"], silent.Items.Select(item => item.Name));
        Assert.Equal(1, silent.Header.Total);
        Assert.Equal(["catalog"], found.Items.Select(item => item.Name));
        Assert.Equal(["orders"], onHost.Items.Select(item => item.Name));
        Assert.Equal(["billing", "catalog", "orders"], byName.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task ListAsync_Limit_PagesWithACursorAndKeepsTheHeaderOfTheWholeSet()
    {
        var first = (await Builder().ListAsync(Query(limit: 2), null, default))!;
        var second = (await Builder().ListAsync(Query(limit: 2, cursor: first.Next), null, default))!;

        Assert.Equal(["orders", "billing"], first.Items.Select(item => item.Name));
        Assert.Equal(["catalog"], second.Items.Select(item => item.Name));
        Assert.Equal((3, 3), (first.Header.Total, second.Header.Total));
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task ListAsync_NoEarlierData_ChangesAreNull()
    {
        var list = (await Builder().ListAsync(Query(range: "24h"), null, default))!;

        Assert.Null(list.Header.RequestsChange);
        Assert.Null(list.Header.ErrorRatioChange);
    }

    private ServiceListBuilder Builder() => new(
        _store,
        _alerts,
        new FixedOptions<QueryOptions>(new QueryOptions()),
        new FakeTimeProvider(_now));

    private static ResourceListQuery Query(
        string? health = null,
        string? search = null,
        string? sort = null,
        int limit = 50,
        string? cursor = null,
        string range = "1h") =>
        new(
            "production",
            range,
            ResourceHealthFilter.Parse(health is null ? null : [health])!,
            search,
            sort,
            cursor,
            limit);

    private static MetricSeries Service(MetricSeries series) =>
        series with { Block = new TelemetryBlock(AlertBlockKind.Service, series.ServiceName!) };

    private static MetricSeries Runtime(string service, string environment, DateTimeOffset seen) => new(
        0,
        "dotnet.gc.collections",
        MetricKind.Sum,
        "{collection}",
        string.Empty,
        MetricTemporality.Cumulative,
        true,
        service,
        environment,
        "web-02",
        new Dictionary<string, string> { ["service.name"] = service, ["host.name"] = "web-02" },
        new Dictionary<string, string>(),
        seen.AddDays(-1),
        seen,
        new TelemetryBlock(AlertBlockKind.Service, service));

    private static AlertState Alert(AlertSubject subject, AlertSeverity severity) => new(
        Guid.NewGuid(),
        "error-rate",
        "production",
        subject,
        severity,
        0.05,
        0.05,
        0.02,
        TimeSpan.FromMinutes(5),
        _now.AddMinutes(-10),
        _now,
        null);
}
