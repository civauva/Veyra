using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using Veyra.Testing;

namespace Veyra.Api.Tests.Metrics;

[Collection(PostgresGroup.Name)]
public class MetricsEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GrpcExport_ThenNamesAndChart_ShowTheMetric()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var key = await CreateKeyAsync(admin);
        using var channel = GrpcChannel.ForAddress(
            "https://localhost",
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });

        var response = await new MetricsService.MetricsServiceClient(channel).ExportAsync(
            Request(Gauge("system.cpu.load_average.1m", 1.5)),
            new Metadata { { "authorization", $"Bearer {key}" } });

        Assert.Null(response.PartialSuccess);
        var name = await WaitForNameAsync(admin, "system.cpu.load_average.1m");
        Assert.Equal(("Gauge", 1), (name.Kind, name.Series));
        Assert.Equal(["web-01"], name.Sources);

        var chart = await admin.GetFromJsonAsync<Chart>(
            "/api/v1/metrics/chart?name=system.cpu.load_average.1m&range=1h&split=host.name");
        Assert.Equal("value", chart!.Show);
        Assert.Equal("web-01", Assert.Single(chart.Lines).Label);
    }

    [Fact]
    public async Task HttpJsonExport_UnsupportedKind_ReportsPartialSuccess()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var key = await CreateKeyAsync(admin);
        using var sender = factory.CreateHttpsClient();
        sender.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        const string json =
            """
            {"resourceMetrics":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"orders"}}]},
             "scopeMetrics":[{"metrics":[
               {"name":"jobs","sum":{"aggregationTemporality":2,"isMonotonic":true,
                 "dataPoints":[{"timeUnixNano":"1762776000000000000","asInt":"7"}]}},
               {"name":"sizes","summary":{"dataPoints":[{"count":"1"}]}}]}]}]}
            """;
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await sender.PostAsync(new Uri("/v1/metrics", UriKind.Relative), content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var answer = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal("1", answer.RootElement.GetProperty("partialSuccess").GetProperty("rejectedDataPoints").GetString());
        Assert.Equal("Sum", (await WaitForNameAsync(admin, "jobs")).Kind);
    }

    [Fact]
    public async Task HttpExport_NoKey_IsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var sender = factory.CreateHttpsClient();
        using var content = new ByteArrayContent(Request(Gauge("x", 1)).ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        using var response = await sender.PostAsync(new Uri("/v1/metrics", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Overview_Anonymous_IsUnauthorizedAndSignedIn_IsEmptyWithoutMetrics()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();
        using var admin = await factory.CreateAdminClientAsync();

        using var refused = await anonymous.GetAsync(new Uri("/api/v1/metrics/overview", UriKind.Relative));
        var overview = await admin.GetFromJsonAsync<Overview>("/api/v1/metrics/overview?range=24h");
        using var unknownHost = await admin.GetAsync(new Uri("/api/v1/metrics/hosts/nowhere", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(("24h", 3600), (overview!.Range, overview.BucketSeconds));
        Assert.Empty(overview.Hosts);
        Assert.Empty(overview.Services);
        Assert.Equal(HttpStatusCode.NotFound, unknownHost.StatusCode);
    }

    [Fact]
    public async Task DockerStatsExport_ThenOverviewAndHost_ShowTheContainerOnItsHost()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var key = await CreateKeyAsync(admin);
        using var channel = GrpcChannel.ForAddress(
            "https://localhost",
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        var request = Request(Sum("system.memory.usage", 4e9, monotonic: false, ("state", "used")));
        request.ResourceMetrics.Add(DockerStats(
            "shop-api",
            Gauge("container.cpu.utilization", 12.5),
            Sum("container.memory.usage.total", 512e6, monotonic: false),
            Sum("container.memory.usage.limit", 1024e6, monotonic: false),
            Gauge("container.memory.percent", 50),
            Sum("container.restarts", 3, monotonic: true),
            Gauge("container.uptime", 600)));

        await new MetricsService.MetricsServiceClient(channel).ExportAsync(
            request,
            new Metadata { { "authorization", $"Bearer {key}" } });

        var card = Assert.Single((await WaitForContainersAsync(admin)).Containers);
        Assert.Equal(
            ("shop-api", "web-01", "shop/api:1.4", "4f2c9a1b7e3d"),
            (card.Name, card.Host, card.Image, card.Id));
        var host = await admin.GetFromJsonAsync<HostView>("/api/v1/metrics/hosts/web-01?range=1h");
        var row = Assert.Single(host!.Containers);
        Assert.Equal(
            (12.5, 512e6, 1024e6, 0.5),
            (row.CpuPercent, row.MemoryBytes, row.MemoryLimitBytes, row.MemoryRatio));
        Assert.Equal((3d, 600d), (row.RestartsTotal, row.UptimeSeconds));
    }

    [Fact]
    public async Task PostgresExport_ThenOverviewAndScreen_ShowTheServerItsDatabasesAndTables()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var key = await CreateKeyAsync(admin);
        using var channel = GrpcChannel.ForAddress(
            "https://localhost",
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        var request = new ExportMetricsServiceRequest();

        // As the tested Collector configuration sends them: the receiver's metrics per server, per database on the
        // resource, and per table; sqlquery's with the database on the point.
        request.ResourceMetrics.Add(Postgres(
            [],
            Sum("postgresql.connection.max", 100, monotonic: false),
            Gauge("postgresql.sessions.blocked", 2),
            Gauge(
                "postgresql.database.xid.age",
                1_116_691_497,
                ("postgresql.database.name", "shop")),
            Gauge("postgresql.query.duration.max", 42, ("postgresql.database.name", "shop"))));
        request.ResourceMetrics.Add(Postgres(
            [("postgresql.database.name", "shop")],
            Sum("postgresql.backends", 86, monotonic: false),
            Sum("postgresql.db_size", 412e9, monotonic: false),
            Sum("postgresql.commits", 90_000, monotonic: true),
            Sum("postgresql.deadlocks", 3, monotonic: true)));
        request.ResourceMetrics.Add(Postgres(
            [
                ("postgresql.database.name", "shop"),
                ("postgresql.schema.name", "public"),
                ("postgresql.table.name", "carts"),
            ],
            Sum("postgresql.rows", 150, monotonic: false, ("state", "dead")),
            Sum("postgresql.rows", 50, monotonic: false, ("state", "live")),
            Sum("postgresql.table.vacuum.count", 12, monotonic: true)));

        await new MetricsService.MetricsServiceClient(channel).ExportAsync(
            request,
            new Metadata { { "authorization", $"Bearer {key}" } });

        var card = await WaitForDatabaseAsync(admin);
        Assert.Equal(("postgresql", "pg-main"), (card.Engine, card.Server));
        Assert.Equal((86d, 100d, 2d), (card.Connections, card.MaxConnections, card.Blocked));
        Assert.Equal(0.52, card.WraparoundRatio!.Value, 6);

        var screen = await admin.GetFromJsonAsync<DatabaseScreen>(
            "/api/v1/metrics/databases/postgresql/pg-main?range=1h");
        var shop = Assert.Single(screen!.Databases);
        Assert.Equal(("shop", 412e9, 86d), (shop.Name, shop.SizeBytes, shop.Connections));
        Assert.Equal(0.52, shop.WraparoundRatio!.Value, 6);
        Assert.Equal((150d, 0.75), (shop.DeadRows, shop.DeadRowsRatio));
        var table = Assert.Single(screen.Tables);
        Assert.Equal(("shop", "public", "carts", 150d), (table.Database, table.Schema, table.Name, table.DeadRows));
        Assert.Equal(42d, screen.Card.LongestQuerySeconds);
    }

    private static async Task<string> CreateKeyAsync(HttpClient admin)
    {
        using var response = await admin.PostAsJsonAsync("/api/v1/ingestion-keys", new { name = "metrics sender" });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedKey>())!.Secret;
    }

    // The writer stores points in the background; wait until the name shows up.
    private static async Task<MetricNameInfo> WaitForNameAsync(HttpClient admin, string metric)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var names = await admin.GetFromJsonAsync<List<MetricNameInfo>>($"/api/v1/metrics/names?search={metric}&range=1h");

            if (names!.FirstOrDefault(name => name.Name == metric) is { } found)
            {
                return found;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"{metric} was not stored.");
    }

    // The writer stores points in the background; wait until the overview has a container with its CPU.
    private static async Task<Overview> WaitForContainersAsync(HttpClient admin)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var overview = await admin.GetFromJsonAsync<Overview>("/api/v1/metrics/overview?range=1h");

            if (overview!.Containers.Any(container => container.CpuPercent is not null))
            {
                return overview;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The container was not stored.");
    }

    // A container's metrics as the Collector's docker_stats receiver sends them, with host.name added.
    private static ResourceMetrics DockerStats(string container, params Metric[] metrics)
    {
        var resource = new Resource();
        resource.Attributes.Add(Text("container.name", container));
        resource.Attributes.Add(Text("container.id", "4f2c9a1b7e3d" + new string('0', 52)));
        resource.Attributes.Add(Text("container.image.name", "shop/api:1.4"));
        resource.Attributes.Add(Text("host.name", "web-01"));

        var scope = new ScopeMetrics();
        scope.Metrics.AddRange(metrics);

        var resourceMetrics = new ResourceMetrics { Resource = resource };
        resourceMetrics.ScopeMetrics.Add(scope);

        return resourceMetrics;
    }

    private static KeyValue Text(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

    private static Metric Sum(string name, double value, bool monotonic, params (string Key, string Value)[] attributes)
    {
        var point = new NumberDataPoint
        {
            StartTimeUnixNano = (ulong)(DateTimeOffset.UtcNow.AddHours(-1) - DateTimeOffset.UnixEpoch).Ticks * 100,
            TimeUnixNano = (ulong)(DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100,
            AsDouble = value,
        };
        point.Attributes.AddRange(attributes.Select(pair => Text(pair.Key, pair.Value)));

        var sum = new Sum
        {
            AggregationTemporality = AggregationTemporality.Cumulative,
            IsMonotonic = monotonic,
        };
        sum.DataPoints.Add(point);

        return new Metric { Name = name, Unit = "1", Sum = sum };
    }

    private static ExportMetricsServiceRequest Request(params Metric[] metrics)
    {
        var resource = new Resource();
        resource.Attributes.Add(new KeyValue { Key = "host.name", Value = new AnyValue { StringValue = "web-01" } });

        var scope = new ScopeMetrics();
        scope.Metrics.AddRange(metrics);

        var resourceMetrics = new ResourceMetrics { Resource = resource };
        resourceMetrics.ScopeMetrics.Add(scope);

        var request = new ExportMetricsServiceRequest();
        request.ResourceMetrics.Add(resourceMetrics);

        return request;
    }

    private static Metric Gauge(string name, double value, params (string Key, string Value)[] attributes)
    {
        var point = new NumberDataPoint
        {
            TimeUnixNano = (ulong)(DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100,
            AsDouble = value,
        };
        point.Attributes.AddRange(attributes.Select(pair => Text(pair.Key, pair.Value)));

        var gauge = new Gauge();
        gauge.DataPoints.Add(point);

        return new Metric { Name = name, Unit = "1", Gauge = gauge };
    }

    // The writer stores points in the background; wait until the overview has the PostgreSQL server's connections.
    private static async Task<DatabaseCard> WaitForDatabaseAsync(HttpClient admin)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var overview = await admin.GetFromJsonAsync<DatabaseOverview>("/api/v1/metrics/overview?range=1h");

            if (overview!.Databases.FirstOrDefault(card => card.Connections is not null) is { } card)
            {
                return card;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The PostgreSQL server was not stored.");
    }

    // A PostgreSQL resource as the tested Collector configuration sends it: the server named, and its own attributes.
    private static ResourceMetrics Postgres((string Key, string Value)[] attributes, params Metric[] metrics)
    {
        var resource = new Resource();
        resource.Attributes.Add(Text("db.system.name", "postgresql"));
        resource.Attributes.Add(Text("server.address", "pg-main"));
        resource.Attributes.AddRange(attributes.Select(pair => Text(pair.Key, pair.Value)));

        var scope = new ScopeMetrics();
        scope.Metrics.AddRange(metrics);

        var resourceMetrics = new ResourceMetrics { Resource = resource };
        resourceMetrics.ScopeMetrics.Add(scope);

        return resourceMetrics;
    }

    private sealed record DatabaseOverview(List<DatabaseCard> Databases);

    private sealed record DatabaseCard(
        string Engine,
        string Server,
        double? Connections,
        double? MaxConnections,
        double? Blocked,
        double? LongestQuerySeconds,
        double? WraparoundRatio);

    private sealed record DatabaseScreen(DatabaseCard Card, List<DatabasePart> Databases, List<DatabaseTable> Tables);

    private sealed record DatabasePart(
        string Name,
        double? SizeBytes,
        double? Connections,
        double? WraparoundRatio,
        double? DeadRows,
        double? DeadRowsRatio);

    private sealed record DatabaseTable(string Database, string Schema, string Name, double? DeadRows);

    private sealed record CreatedKey(string Secret);

    private sealed record MetricNameInfo(string Name, string Kind, string Unit, int Series, List<string> Sources);

    private sealed record Chart(string Show, List<Line> Lines);

    private sealed record Line(string Label, double? Latest);

    private sealed record Overview(
        string Range,
        int BucketSeconds,
        List<object> Hosts,
        List<object> Services,
        List<ContainerCard> Containers);

    private sealed record HostView(List<ContainerCard> Containers);

    private sealed record ContainerCard(
        string Name,
        string? Host,
        string? Image,
        string? Id,
        double? CpuPercent,
        double? MemoryBytes,
        double? MemoryLimitBytes,
        double? MemoryRatio,
        double? RestartsTotal,
        double? UptimeSeconds);
}
