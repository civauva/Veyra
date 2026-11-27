using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Alerting.Alerts;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Testing;
using Xunit.Abstractions;

namespace Veyra.Api.Tests.Query;

/// <summary>
/// The Overview of ADR 0023 against PostgreSQL: one read with every figure, panel, and table, worked out on the
/// server.
/// </summary>
[Collection(PostgresGroup.Name)]
public class OverviewEndpointTests(PostgresFixture postgres, ITestOutputHelper output)
{
    private const string _password = "another long secret";

    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    private static readonly TelemetryBlock _sql = TelemetryBlock.Database("microsoft.sql_server", "SQL-PROD-01");

    private static readonly TelemetryBlock _postgres = TelemetryBlock.Database("postgresql", "pg-main");

    // Three shares alike to two decimals.
    private static readonly IEqualityComparer<(double, double, double)> _close =
        EqualityComparer<(double, double, double)>.Create((one, other) =>
            Math.Abs(one.Item1 - other.Item1) < 0.01
            && Math.Abs(one.Item2 - other.Item2) < 0.01
            && Math.Abs(one.Item3 - other.Item3) < 0.01);

    [Fact]
    public async Task Overview_Estate_HasEveryFigurePanelAndTable()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);

        var overview = await admin.GetFromJsonAsync<Overview>("/api/v1/overview?environment=production&range=1h");

        var figures = overview!.Figures;
        Assert.Equal(new ServicesFigure(1, 1, 0, 0, 0), figures.Services);
        Assert.Equal(new HostsFigure(1, 1), figures.Hosts);
        Assert.Equal(new DatabasesFigure(2, 3), figures.Databases);
        Assert.InRange(figures.Requests.Count!.Value, 600, 700);
        Assert.InRange(figures.Requests.Change!.Value, -0.1, 0.1);
        Assert.Equal(1.0 / 11, figures.ErrorRatio.Value!.Value, 2);
        Assert.InRange(figures.ErrorRatio.Change!.Value, -0.01, 0.01);
        Assert.Equal(new OpenAlertsFigure(0, 1), figures.OpenAlerts);
        Assert.Equal((2.0 / 3, 3, 1), (figures.Jobs.SucceededRatio!.Value, figures.Jobs.Runs, figures.Jobs.Failed));
        Assert.Equal(1 - (2.0 / 720), figures.Availability.Ratio!.Value, 6);
        Assert.Equal(-2.0 / 720, figures.Availability.Change!.Value, 6);
        Assert.Equal(30, figures.Availability.Days);

        Assert.Equal((60, 60), (overview.Activity.StepSeconds, overview.Activity.Points.Count));
        Assert.Contains(overview.Activity.Points, point => point is { Requests: 11, Errors: 1 });
        Assert.Equal(3, overview.Ingestion.Points.Sum(point => point.Logs));
        Assert.Equal(2, overview.Ingestion.Points.Sum(point => point.Spans));
        Assert.True(overview.Ingestion.Points.Sum(point => point.Points) > 0);
        var trend = overview.HostTrends.Points.Last(point => point.Cpu is not null);
        Assert.Equal((0.25, 0.75, 0.89), (trend.Cpu!.Value, trend.Memory!.Value, trend.Disk!.Value), _close);

        Assert.Equal(
            [("Nightly backup", "Failed"), ("Index rebuild", "Running")],
            overview.Pipelines.Select(pipeline => (pipeline.Name, pipeline.Status)));
        var backup = overview.Pipelines[0];
        Assert.Equal(
            ("SQL-PROD-01", "microsoft.sql_server", 2, 1),
            (backup.Server, backup.Engine, backup.Runs, backup.FailedRuns));
        Assert.Equal(120, overview.Pipelines[1].DurationSeconds, 0);
        Assert.NotEqual(backup.JobId, overview.Pipelines[1].JobId);
        using var job = await admin.GetAsync(new Uri($"/api/v1/jobs/{backup.JobId}?range=1h", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, job.StatusCode);

        Assert.NotNull(overview.Collectors);
        Assert.All(overview.Collectors, collector => Assert.Null(collector.Queue));

        Assert.Equal(3, overview.Databases.Total);
        Assert.Equal(["Reporting", "Shop", "app"], overview.Databases.Items.Select(item => item.Name));
        var reporting = overview.Databases.Items[0];
        Assert.Equal(
            ("SQL-PROD-01", "Online", true, 200_000d),
            (reporting.Server, reporting.State, reporting.Late, reporting.LastFullBackupAgeSeconds));
        var app = overview.Databases.Items[2];
        Assert.Equal(("pg-main", null, false, 7d), (app.Server, app.State, app.Late, app.Connections));
    }

    [Fact]
    public async Task Overview_EmptyEstate_HasEveryBlockWithNothingInIt()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        var overview = await admin.GetFromJsonAsync<Overview>("/api/v1/overview?range=24h");

        var figures = overview!.Figures;
        Assert.Equal(new ServicesFigure(0, 0, 0, 0, 0), figures.Services);
        Assert.Equal(new HostsFigure(0, 0), figures.Hosts);
        Assert.Equal(new DatabasesFigure(0, 0), figures.Databases);
        Assert.Equal(new RequestsFigure(null, null), figures.Requests);
        Assert.Equal(new ErrorRatioFigure(null, null), figures.ErrorRatio);
        Assert.Equal(new JobsFigure(null, 0, 0), figures.Jobs);
        Assert.Equal(new AvailabilityFigure(null, null, 30), figures.Availability);
        Assert.Equal((3600, 24), (overview.Activity.StepSeconds, overview.Activity.Points.Count));
        Assert.All(overview.Activity.Points, point => Assert.Null(point.Requests));
        Assert.All(
            overview.Ingestion.Points,
            point => Assert.Equal((0L, 0L, 0L), (point.Logs, point.Spans, point.Points)));
        Assert.All(overview.HostTrends.Points, point => Assert.Null(point.Cpu));
        Assert.Empty(overview.Pipelines);
        Assert.Empty(overview.Databases.Items);
        Assert.Equal(0, overview.Databases.Total);
        Assert.Equal(_now, overview.To, TimeSpan.FromMinutes(1));
        Assert.Equal(_now.AddHours(-24), overview.From, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Overview_Viewer_GetsEverythingButTheCollectors()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var created = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "ana@example.com", displayName = "Ana", role = "Viewer", password = _password });
        created.EnsureSuccessStatusCode();
        using var viewer = await factory.CreateSignedInClientAsync("ana@example.com", _password);

        var json = await viewer.GetStringAsync(new Uri("/api/v1/overview", UriKind.Relative));
        var overview = await viewer.GetFromJsonAsync<Overview>("/api/v1/overview");

        Assert.Contains("\"collectors\":null", json, StringComparison.Ordinal);
        Assert.Null(overview!.Collectors);
        Assert.NotNull(overview.Figures);
    }

    [Theory]
    [InlineData(-32, 0)]
    [InlineData(0, -1)]
    public async Task Overview_WindowLongerThanThirtyOneDaysOrBackwards_IsAProblem(int fromDays, int toDays)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(
            $"/api/v1/overview?from={Text(_now.AddDays(fromDays))}&to={Text(_now.AddDays(toDays))}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Overview_RowsAsked_ListsThatManyDatabasesAndPipelines()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);

        var overview = await admin.GetFromJsonAsync<Overview>(
            "/api/v1/overview?environment=production&range=1h&databases=1&pipelines=1");

        Assert.Single(overview!.Databases.Items);
        Assert.Equal(3, overview.Databases.Total);
        Assert.Single(overview.Pipelines);
    }

    [Theory]
    [InlineData("databases=0")]
    [InlineData("databases=51")]
    [InlineData("pipelines=0")]
    [InlineData("pipelines=51")]
    public async Task Overview_RowsOutOfRange_IsAProblem(string rows)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri($"/api/v1/overview?{rows}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Overview_SignedOut_IsRefused()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri("/api/v1/overview", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Overview_Estate_IsReadQuickly()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);
        var json = await admin.GetStringAsync(new Uri("/api/v1/overview?range=1h", UriKind.Relative));
        var times = new List<double>();

        foreach (var range in new[] { "1h", "1h", "1h", "24h", "7d", "30d" })
        {
            var watch = Stopwatch.StartNew();
            using var response = await admin.GetAsync(new Uri($"/api/v1/overview?range={range}", UriKind.Relative));
            response.EnsureSuccessStatusCode();
            times.Add(watch.Elapsed.TotalMilliseconds);
            output.WriteLine($"{range}: {watch.Elapsed.TotalMilliseconds:F0} ms");
        }

        output.WriteLine(json);
        Assert.All(times, time => Assert.True(time < 2_000, $"The Overview took {time:F0} ms."));
    }

    private static string Text(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    // One service with HTTP metrics, one host, a SQL Server with three jobs and a PostgreSQL server, a few logs and
    // spans, a warning on the service that lasted two hours, and a caution open on the host.
    private static async Task SeedAsync(VeyraApiFactory factory)
    {
        var metrics = factory.Services.GetRequiredService<IMetricWriter>();
        await metrics.WriteAsync(
            [.. Requests("orders", 200, 10), .. Requests("orders", 500, 1)],
            CancellationToken.None);
        await metrics.WriteAsync([.. HostMetrics("web-01")], CancellationToken.None);
        await metrics.WriteAsync(
            [
                Database(_sql, "sqlserver.database.size", 10e9, ("database_name", "Shop")),
                Database(_sql, "sqlserver.database.size", 5e9, ("database_name", "Reporting")),
                Database(_sql, "sqlserver.database.size", 1e6, ("database_name", "master")),
                Database(_sql, "sqlserver.database.backup.age", 21_600, ("database_name", "Shop")),
                Database(_sql, "sqlserver.database.backup.age", 200_000, ("database_name", "Reporting")),
                Database(_sql, "sqlserver.database.offline", 0, ("database_name", "Shop"), ("state", "ONLINE")),
                Database(_sql, "sqlserver.database.offline", 0, ("database_name", "Reporting"), ("state", "ONLINE")),
                Database(_sql, "sqlserver.agent.job.running", 120, ("job_name", "Index rebuild")),
                Database(_sql, "sqlserver.agent.job.running", -1, ("job_name", "Nightly backup")),
                Database(_postgres, "postgresql.db_size", 2e9, ("postgresql.database.name", "app")),
                Database(_postgres, "postgresql.backends", 7, ("postgresql.database.name", "app")),
            ],
            CancellationToken.None);

        await factory.Services.GetRequiredService<IDatabaseActivityWriter>().WriteAsync(
            [
                Run("1", "Nightly backup", "0", "0", _now.AddMinutes(-10)),
                Run("2", "Nightly backup", "1", "0", _now.AddMinutes(-10)),
                Run("3", "Nightly backup", "0", "1", _now.AddMinutes(-40)),
                Run("4", "Index rebuild", "0", "1", _now.AddMinutes(-30)),
            ],
            CancellationToken.None);

        var logs = factory.Services.GetRequiredService<ILogWriter>();
        await logs.WriteAsync([Log(_now.AddMinutes(-5)), Log(_now.AddMinutes(-4)), Log(_now.AddMinutes(-3))], default);
        await factory.Services.GetRequiredService<ISpanWriter>().WriteAsync(
            [Span("00000000000000a1", _now.AddMinutes(-2)), Span("00000000000000a2", _now.AddMinutes(-1))],
            CancellationToken.None);

        var alerts = factory.Services.GetRequiredService<AlertStore>();
        var warning = Alert(AlertSubject.Service("orders"), AlertSeverity.Warning, _now.AddHours(-3));
        await alerts.InsertAsync(warning, CancellationToken.None);
        await alerts.ResolveAsync(warning.Id, 0, _now.AddHours(-1), CancellationToken.None);
        await alerts.InsertAsync(
            Alert(AlertSubject.Host("web-01", "/"), AlertSeverity.Caution, _now.AddMinutes(-20)) with
            {
                Rule = AlertRules.DiskSpace,
            },
            CancellationToken.None);
    }

    private static AlertState Alert(AlertSubject subject, AlertSeverity severity, DateTimeOffset openedAt) => new(
        Guid.CreateVersion7(),
        AlertRules.ErrorRate,
        "production",
        subject,
        severity,
        0.2,
        0.2,
        0.05,
        TimeSpan.FromMinutes(5),
        openedAt,
        openedAt,
        null);

    private static ActivityRecord Run(string key, string job, string step, string status, DateTimeOffset at) => new(
        _sql.Name,
        null,
        at,
        "sqlserver.job",
        key,
        new Dictionary<string, string>
        {
            ["job_name"] = job,
            ["step_id"] = step,
            ["step_name"] = step == "0" ? string.Empty : "Back up",
            ["run_status"] = status,
            ["duration"] = "42",
        },
        status == "1" ? "The job succeeded." : "The job failed.");

    private static LogRecord Log(DateTimeOffset at) => new(
        at,
        at,
        LogSeverity.Information,
        "Information",
        "order placed",
        "orders",
        "production",
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = "orders" });

    private static Span Span(string spanId, DateTimeOffset start) => new(
        "4bf92f3577b34da6a3ce929d0e0e4736",
        spanId,
        null,
        "GET /orders",
        SpanKind.Server,
        start,
        TimeSpan.FromMilliseconds(20),
        SpanStatus.Unset,
        null,
        "orders",
        "production",
        "web-01",
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = "orders" },
        [],
        [],
        SpanDropped.None);

    // Two hours of a cumulative request duration histogram, one point a minute, growing by the given requests.
    private static IEnumerable<MetricPoint> Requests(string service, int status, long perMinute) =>
        Enumerable.Range(0, 121).Select(minute => new MetricPoint(
            "http.server.request.duration",
            MetricKind.Histogram,
            "s",
            string.Empty,
            MetricTemporality.Cumulative,
            false,
            service,
            "production",
            "web-01",
            new Dictionary<string, string>
            {
                ["service.name"] = service,
                ["service.instance.id"] = service + "-1",
                ["host.name"] = "web-01",
            },
            new Dictionary<string, string>
            {
                ["http.response.status_code"] = status.ToString(CultureInfo.InvariantCulture),
                ["http.route"] = "/orders",
                ["http.request.method"] = "GET",
            },
            _now.AddMinutes(minute - 120),
            _now.AddHours(-3),
            null,
            new HistogramValue(perMinute * minute, null, null, null, [0.1, 1], [perMinute * minute, 0, 0]),
            new TelemetryBlock(AlertBlockKind.Service, service)));

    // An hour of a host's CPU (a quarter busy), memory (three quarters used), and its disk (89 % full).
    private static IEnumerable<MetricPoint> HostMetrics(string host) =>
        Enumerable.Range(0, 61).SelectMany(minute => new[]
        {
            HostPoint(host, "system.cpu.time", true, 15.0 * minute, minute, ("cpu", "0"), ("state", "user")),
            HostPoint(host, "system.cpu.time", true, 45.0 * minute, minute, ("cpu", "0"), ("state", "idle")),
            HostPoint(host, "system.memory.usage", false, 6e9, minute, ("state", "used")),
            HostPoint(host, "system.memory.usage", false, 2e9, minute, ("state", "free")),
            HostPoint(host, "system.filesystem.usage", false, 89e9, minute, ("mountpoint", "/"), ("state", "used")),
            HostPoint(host, "system.filesystem.usage", false, 11e9, minute, ("mountpoint", "/"), ("state", "free")),
        });

    private static MetricPoint HostPoint(
        string host,
        string name,
        bool monotonic,
        double value,
        int minute,
        params (string Key, string Value)[] attributes) => new(
        name,
        MetricKind.Sum,
        "By",
        string.Empty,
        MetricTemporality.Cumulative,
        monotonic,
        null,
        null,
        host,
        new Dictionary<string, string> { ["host.name"] = host, ["os.description"] = "Ubuntu 24.04" },
        attributes.ToDictionary(pair => pair.Key, pair => pair.Value),
        _now.AddMinutes(minute - 60),
        _now.AddDays(-1),
        value,
        null,
        new TelemetryBlock(AlertBlockKind.Host, host));

    private static MetricPoint Database(
        TelemetryBlock block,
        string name,
        double value,
        params (string Key, string Value)[] attributes) => new(
        name,
        MetricKind.Gauge,
        "1",
        string.Empty,
        MetricTemporality.Unspecified,
        false,
        null,
        null,
        "db-host",
        new Dictionary<string, string> { ["db.system.name"] = block.Engine!, ["server.address"] = block.Server! },
        attributes.ToDictionary(pair => pair.Key, pair => pair.Value),
        _now.AddMinutes(-1),
        null,
        value,
        null,
        block);

    private sealed record Overview(
        DateTimeOffset From,
        DateTimeOffset To,
        Figures Figures,
        Series<ActivityPoint> Activity,
        Series<IngestionPoint> Ingestion,
        Series<HostTrendPoint> HostTrends,
        List<Pipeline> Pipelines,
        List<Collector>? Collectors,
        Databases Databases);

    private sealed record Figures(
        ServicesFigure Services,
        HostsFigure Hosts,
        DatabasesFigure Databases,
        RequestsFigure Requests,
        ErrorRatioFigure ErrorRatio,
        OpenAlertsFigure OpenAlerts,
        JobsFigure Jobs,
        AvailabilityFigure Availability);

    private sealed record ServicesFigure(int Healthy, int Total, int Warning, int Caution, int Silent);

    private sealed record HostsFigure(int Online, int Known);

    private sealed record DatabasesFigure(int Servers, int Databases);

    private sealed record RequestsFigure(long? Count, double? Change);

    private sealed record ErrorRatioFigure(double? Value, double? Change);

    private sealed record OpenAlertsFigure(int Warning, int Caution);

    private sealed record JobsFigure(double? SucceededRatio, int Runs, int Failed);

    private sealed record AvailabilityFigure(double? Ratio, double? Change, int Days);

    private sealed record Series<TPoint>(int StepSeconds, List<TPoint> Points);

    private sealed record ActivityPoint(DateTimeOffset At, long? Requests, long? Errors);

    private sealed record IngestionPoint(DateTimeOffset At, long Logs, long Spans, long Points);

    private sealed record HostTrendPoint(DateTimeOffset At, double? Cpu, double? Memory, double? Disk);

    private sealed record Pipeline(
        string Name,
        string Server,
        string? Engine,
        DateTimeOffset LastRunAt,
        double DurationSeconds,
        string Status,
        int Runs,
        int FailedRuns,
        string JobId);

    private sealed record Collector(string Name, string State, DateTimeOffset? LastSeenAt, string? Version, int? Queue);

    private sealed record Databases(List<DatabaseItem> Items, int Total);

    private sealed record DatabaseItem(
        string Name,
        string Server,
        string Engine,
        string? State,
        double? SizeBytes,
        double? GrowthBytesPerDay,
        double? LastFullBackupAgeSeconds,
        bool Late,
        double? Connections);
}
