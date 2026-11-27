using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Api.Tests.Query;

/// <summary>
/// The Resources lists and the metrics' <c>from</c> and <c>to</c> (ADR 0022), against PostgreSQL: every filter, sort,
/// count, and comparison is worked out on the server.
/// </summary>
[Collection(PostgresGroup.Name)]
public class ResourcesEndpointTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Services_WorstFirst_SilentFirstWithTheHeaderOfTheWholeSetAndItsChange()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await WriteAsync(factory, [.. Requests("orders", 200, 10), .. Requests("orders", 500, 1)]);
        await WriteAsync(factory, [Gauge("dotnet.gc.heap", "billing"), Gauge("dotnet.gc.heap", "catalog")]);
        await AgeAsync(factory, "billing", TimeSpan.FromHours(3));

        var list = await admin.GetFromJsonAsync<Services>("/api/v1/resources/services?environment=production&range=1h");

        Assert.Equal(["billing", "catalog", "orders"], list!.Items.Select(item => item.Name));
        Assert.Equal((true, "Ok"), (list.Items[0].Silent, list.Items[0].Health));
        Assert.Null(list.Items[0].RequestsPerMinute);
        var orders = list.Items[2];
        Assert.Equal(11, orders.RequestsPerMinute!.Value, 0);
        Assert.Equal(1.0 / 11, orders.ErrorRatio!.Value, 2);
        Assert.Equal(["web-01"], orders.Hosts);
        Assert.Equal(24, orders.Requests.Count);
        var header = list.Header;
        Assert.Equal((3, 3, 1), (header.Total, header.Healthy, header.Silent));
        Assert.InRange(header.Requests!.Value, 600, 700);
        Assert.InRange(header.RequestsChange!.Value, -0.1, 0.1);
        Assert.InRange(header.ErrorRatioChange!.Value, -0.01, 0.01);
        Assert.NotNull(header.P95Seconds);
        Assert.Equal((3, 2), (header.Instances, header.Hosts));
        Assert.Equal(3, list.Total);
    }

    [Fact]
    public async Task Services_FiltersAndPaging_AreAppliedOnTheServer()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await WriteAsync(factory, [.. Requests("orders", 200, 10)]);
        await WriteAsync(factory, [Gauge("dotnet.gc.heap", "billing"), Gauge("dotnet.gc.heap", "catalog")]);
        await WriteAsync(factory, [Gauge("dotnet.gc.heap", "search") with { Environment = "staging" }]);
        await AgeAsync(factory, "billing", TimeSpan.FromHours(3));

        var silent = await admin.GetFromJsonAsync<Services>("/api/v1/resources/services?health=silent");
        var found = await admin.GetFromJsonAsync<Services>("/api/v1/resources/services?search=CAT");
        var onHost = await admin.GetFromJsonAsync<Services>("/api/v1/resources/services?host=web-01");
        var staging = await admin.GetFromJsonAsync<Services>("/api/v1/resources/services?environment=staging");
        var first = await admin.GetFromJsonAsync<Services>("/api/v1/resources/services?sort=name&limit=2");
        var second = await admin.GetFromJsonAsync<Services>(
            $"/api/v1/resources/services?sort=name&limit=2&cursor={first!.Next}");

        Assert.Equal(["billing"], silent!.Items.Select(item => item.Name));
        Assert.Equal(["catalog"], found!.Items.Select(item => item.Name));
        Assert.Equal(["orders"], onHost!.Items.Select(item => item.Name));
        Assert.Equal(["search"], staging!.Items.Select(item => item.Name));
        Assert.Equal(["billing", "catalog"], first.Items.Select(item => item.Name));
        Assert.Equal(4, first.Total);
        Assert.Equal(4, first.Header.Total);
        Assert.Equal(["orders", "search"], second!.Items.Select(item => item.Name));
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task Hosts_SilentHostStaysListedAndCountsAsNotOnline()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await WriteAsync(
            factory,
            [.. HostMetrics("web-01"), .. HostMetrics("sql-01"), Container("shop-api", "web-01")]);
        await WriteAsync(factory, [.. Requests("orders", 200, 10)]);
        await AgeAsync(factory, "sql-01", TimeSpan.FromMinutes(10));

        var list = await admin.GetFromJsonAsync<Hosts>("/api/v1/resources/hosts?range=1h");
        var silent = await admin.GetFromJsonAsync<Hosts>("/api/v1/resources/hosts?state=silent");
        var containers = await admin.GetFromJsonAsync<Hosts>("/api/v1/resources/hosts?kind=container");

        Assert.Equal(["sql-01", "shop-api", "web-01"], list!.Items.Select(item => item.Name));
        Assert.Equal((2, 1, 1), (list.Header.Known, list.Header.Online, list.Header.Containers));
        var web = list.Items[2];
        Assert.Equal(("host", false, "Ubuntu 24.04"), (web.Kind, web.Silent, web.Os));
        Assert.Equal(0.25, web.CpuRatio!.Value, 2);
        Assert.Equal(0.75, web.MemoryRatio!.Value, 2);
        Assert.Equal(0.89, web.FullestDiskRatio!.Value, 2);
        Assert.Equal(["orders", "shop-api"], web.Runs);
        Assert.Equal(("container", "web-01"), (list.Items[1].Kind, list.Items[1].Parent));
        Assert.Equal(["sql-01"], silent!.Items.Select(item => item.Name));
        Assert.Equal(["shop-api"], containers!.Items.Select(item => item.Name));
        Assert.Equal((0, 1), (containers.Header.Known, containers.Header.Containers));
    }

    [Fact]
    public async Task Hosts_SilentAfterSetting_DecidesWhenAHostIsSilent()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(
            postgres,
            ("Veyra:Query:SilentAfterHosts", "00:30:00"));
        using var admin = await factory.CreateAdminClientAsync();
        await WriteAsync(factory, [.. HostMetrics("sql-01")]);
        await AgeAsync(factory, "sql-01", TimeSpan.FromMinutes(10));

        var list = await admin.GetFromJsonAsync<Hosts>("/api/v1/resources/hosts");

        Assert.False(Assert.Single(list!.Items).Silent);
        Assert.Equal(1, list.Header.Online);
    }

    [Fact]
    public async Task Databases_ServersWithDatabasesBackupsAndVersions()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var sql = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");
        await WriteAsync(
            factory,
            [
                Database(sql, "sqlserver.server.info", 1, ("version", "16.0.4135.4"), ("update_level", "CU14")),
                Database(sql, "sqlserver.database.size", 10e9, ("database_name", "shop")),
                Database(sql, "sqlserver.database.size", 5e9, ("database_name", "Reporting")),
                Database(sql, "sqlserver.database.size", 1e6, ("database_name", "master")),
                Database(sql, "sqlserver.database.backup.age", 21_600, ("database_name", "shop")),
                Database(sql, "sqlserver.database.backup.age", 200_000, ("database_name", "Reporting")),
                Database(
                    TelemetryBlock.Database("postgresql", "pg-main"),
                    "postgresql.db_size",
                    2e9,
                    ("postgresql.database.name", "app")),
            ]);

        var list = await admin.GetFromJsonAsync<Databases>("/api/v1/resources/databases?sort=name");
        var withSystem = await admin.GetFromJsonAsync<Databases>(
            "/api/v1/resources/databases?engine=microsoft.sql_server&system=true");

        Assert.Equal(["pg-main", "sql-prod-01"], list!.Items.Select(item => item.Server));
        var server = list.Items[1];
        Assert.Equal(("SQL Server 2022 CU14", 2, 15e9), (server.Version, server.Databases, server.SizeBytes));
        Assert.Equal((200_000d, 1), (server.LastFullBackupAgeSeconds, server.LateBackups));
        Assert.Null(server.BlockingChains);
        var header = list.Header;
        Assert.Equal((2, 3, 17e9, 1), (header.Servers, header.Databases, header.DataBytes, header.LateBackups));
        Assert.Equal("Reporting", list.Header.LateBackupFirst!.Database);
        Assert.Equal(3, Assert.Single(withSystem!.Items).Databases);
    }

    [Theory]
    [InlineData("/api/v1/resources/services?limit=0")]
    [InlineData("/api/v1/resources/services?sort=busiest")]
    [InlineData("/api/v1/resources/services?health=red")]
    [InlineData("/api/v1/resources/services?cursor=bm90IG91cnM")]
    [InlineData("/api/v1/resources/hosts?kind=vm")]
    [InlineData("/api/v1/resources/hosts?state=asleep")]
    public async Task Lists_UnknownParameter_IsAProblem(string path)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FromTo_StepIsMinutesUpToSixHoursAndHoursBeyond()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await WriteAsync(factory, [.. HostMetrics("web-01")]);
        var to = Text(_now);

        var six = await admin.GetFromJsonAsync<Chart>(
            $"/api/v1/metrics/chart?name=system.memory.usage&from={Text(_now.AddHours(-6))}&to={to}");
        var seven = await admin.GetFromJsonAsync<Chart>(
            $"/api/v1/metrics/chart?name=system.memory.usage&from={Text(_now.AddHours(-7))}&to={to}");
        var host = await admin.GetFromJsonAsync<Window>(
            $"/api/v1/metrics/hosts/web-01?from={Text(_now.AddHours(-2))}&to={to}");
        var overview = await admin.GetFromJsonAsync<Window>(
            $"/api/v1/metrics/overview?from={Text(_now.AddDays(-30))}&to={to}");

        Assert.Equal(300, six!.BucketSeconds);
        Assert.Equal(3600, seven!.BucketSeconds);
        Assert.Equal(("custom", 60), (host!.Range, host.BucketSeconds));
        Assert.Equal(_now.AddHours(-2), host.From, TimeSpan.FromSeconds(1));
        Assert.Equal(_now, host.To, TimeSpan.FromSeconds(1));
        Assert.Equal(43_200, overview!.BucketSeconds);
    }

    [Theory]
    [InlineData("/api/v1/metrics/overview")]
    [InlineData("/api/v1/metrics/chart?name=system.memory.usage")]
    [InlineData("/api/v1/metrics/names")]
    [InlineData("/api/v1/metrics/hosts/web-01")]
    [InlineData("/api/v1/metrics/databases/microsoft.sql_server/sql-01/queries")]
    [InlineData("/api/v1/resources/services")]
    [InlineData("/api/v1/resources/hosts")]
    [InlineData("/api/v1/resources/databases")]
    public async Task FromTo_LongerThanThirtyOneDays_IsAProblem(string path)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        using var tooLong = await admin.GetAsync(new Uri(
            $"{path}{separator}from={Text(_now.AddDays(-32))}&to={Text(_now)}",
            UriKind.Relative));
        using var backwards = await admin.GetAsync(new Uri(
            $"{path}{separator}from={Text(_now)}&to={Text(_now.AddHours(-1))}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
    }

    private static string Text(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static async Task WriteAsync(VeyraApiFactory factory, MetricPoint[] points) =>
        await factory.Services.GetRequiredService<IMetricWriter>().WriteAsync(points, CancellationToken.None);

    // Storage stamps a series' last-seen time when its points are written; a test makes it older, as time passing does.
    private static async Task AgeAsync(VeyraApiFactory factory, string block, TimeSpan age)
    {
        var dataSource = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var command = dataSource.CreateCommand(
            "UPDATE storage.metric_series SET last_seen = $2 WHERE block_name = $1");
        command.Parameters.AddWithValue(block);
        command.Parameters.AddWithValue(_now - age);

        await command.ExecuteNonQueryAsync();
    }

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

    private static MetricPoint Gauge(string name, string service) => new(
        name,
        MetricKind.Gauge,
        "By",
        string.Empty,
        MetricTemporality.Unspecified,
        false,
        service,
        "production",
        "web-02",
        new Dictionary<string, string> { ["service.name"] = service, ["host.name"] = "web-02" },
        new Dictionary<string, string>(),
        _now,
        null,
        1,
        null,
        new TelemetryBlock(AlertBlockKind.Service, service));

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

    private static MetricPoint Container(string name, string host) => new(
        "container.cpu.utilization",
        MetricKind.Gauge,
        "1",
        string.Empty,
        MetricTemporality.Unspecified,
        false,
        null,
        null,
        host,
        new Dictionary<string, string> { ["container.name"] = name, ["host.name"] = host },
        new Dictionary<string, string>(),
        _now.AddMinutes(-1),
        null,
        50,
        null,
        new TelemetryBlock(AlertBlockKind.Container, name));

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

    private sealed record Services(ServiceHeader Header, List<Service> Items, int Total, string? Next);

    private sealed record ServiceHeader(
        int Total,
        int Healthy,
        int Silent,
        long? Requests,
        double? RequestsChange,
        double? ErrorRatioChange,
        double? P95Seconds,
        int Instances,
        int Hosts);

    private sealed record Service(
        string Name,
        string Health,
        bool Silent,
        double? RequestsPerMinute,
        double? ErrorRatio,
        List<string> Hosts,
        List<double?> Requests);

    private sealed record Hosts(HostHeader Header, List<HostItem> Items, int Total, string? Next);

    private sealed record HostHeader(int Known, int Online, int Containers);

    private sealed record HostItem(
        string Name,
        string Kind,
        string? Os,
        string? Parent,
        bool Silent,
        double? CpuRatio,
        double? MemoryRatio,
        double? FullestDiskRatio,
        List<string> Runs);

    private sealed record Databases(DatabaseHeader Header, List<DatabaseItem> Items);

    private sealed record DatabaseHeader(
        int Servers,
        int Databases,
        double? DataBytes,
        int LateBackups,
        LateBackup? LateBackupFirst);

    private sealed record LateBackup(string Server, string Database);

    private sealed record DatabaseItem(
        string Server,
        string? Version,
        int Databases,
        double? SizeBytes,
        double? LastFullBackupAgeSeconds,
        int? LateBackups,
        int? BlockingChains);

    private sealed record Chart(int BucketSeconds);

    private sealed record Window(string Range, int BucketSeconds, DateTimeOffset From, DateTimeOffset To);
}
