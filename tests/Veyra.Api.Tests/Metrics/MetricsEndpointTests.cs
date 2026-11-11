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
        Assert.Equal(("24h", 900), (overview!.Range, overview.BucketSeconds));
        Assert.Empty(overview.Hosts);
        Assert.Empty(overview.Services);
        Assert.Equal(HttpStatusCode.NotFound, unknownHost.StatusCode);
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

    private static Metric Gauge(string name, double value)
    {
        var gauge = new Gauge();
        gauge.DataPoints.Add(new NumberDataPoint
        {
            TimeUnixNano = (ulong)(DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100,
            AsDouble = value,
        });

        return new Metric { Name = name, Unit = "1", Gauge = gauge };
    }

    private sealed record CreatedKey(string Secret);

    private sealed record MetricNameInfo(string Name, string Kind, string Unit, int Series, List<string> Sources);

    private sealed record Chart(string Show, List<Line> Lines);

    private sealed record Line(string Label, double? Latest);

    private sealed record Overview(string Range, int BucketSeconds, List<object> Hosts, List<object> Services);
}
