using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core.Traces;
using Veyra.Testing;

namespace Veyra.Api.Tests.Query;

/// <summary>
/// Traces end to end (ADR 0014): spans sent over OTLP, then listed, charted, and opened through the API.
/// </summary>
[Collection(PostgresGroup.Name)]
public class TracesEndpointTests(PostgresFixture postgres)
{
    private const string _traceId = "5b8efff798038103d269b633813fc60c";

    [Fact]
    public async Task GetTraces_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri("/api/v1/traces", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostTracesThenGet_JsonWithHexIds_ListsTheTraceAndOpensItWithEverySpan()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var key = await CreateKeyAsync(admin);
        using var sender = factory.CreateClient();
        sender.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);

        using var post = await sender.PostAsync(
            new Uri("/v1/traces", UriKind.Relative),
            new StringContent(CheckoutJson(start), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var list = await WaitForTracesAsync(admin, "/api/v1/traces");

        var item = Assert.Single(list.Items);
        Assert.Equal((_traceId, "POST /api/checkout", "checkout-web"), (item.TraceId, item.Name, item.Service));
        Assert.Equal((true, 3, 2), (item.Failed, item.Spans, item.FailedSpans));
        Assert.Equal(1840, item.DurationMs, 3);
        Assert.Equal(("psp-client", "The PSP did not answer"), (item.Cause!.Service, item.Cause.Message));
        // Each service sent one span; equal counts are listed by name.
        Assert.Equal(["checkout-web", "postgres", "psp-client"], item.Services.Select(share => share.Service));

        var trace = await admin.GetFromJsonAsync<TraceDto>(
            $"/api/v1/traces/{_traceId}?at={Uri.EscapeDataString(start.ToString("O"))}");

        Assert.Equal(3, trace!.Spans.Count);
        var root = trace.Spans[0];
        Assert.Equal(("Server", "Error", (string?)null), (root.Kind, root.Status, root.ParentSpanId));
        var psp = trace.Spans.Single(span => span.Service == "psp-client");
        Assert.Equal(root.SpanId, psp.ParentSpanId);
        Assert.Equal("exception", Assert.Single(psp.Events).Name);
        Assert.Equal("System.TimeoutException", psp.Events[0].Attributes["exception.type"]);

        var errors = await admin.GetFromJsonAsync<TraceDto>(
            $"/api/v1/traces/{_traceId}?errorsOnly=true&at={Uri.EscapeDataString(start.ToString("O"))}");

        Assert.Equal(2, errors!.Spans.Count);
        Assert.All(errors.Spans, span => Assert.Equal("Error", span.Status));
        Assert.Equal((3, 2), (errors.Summary.Spans, errors.Summary.Failed));
        Assert.Equal(1840, errors.Summary.DurationMs, 3);
    }

    [Fact]
    public async Task GetTraces_SmallLimit_PagesWithACursor()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(factory, Root(1, now.AddMinutes(-2), 100), Root(2, now.AddMinutes(-1), 100));

        var first = await admin.GetFromJsonAsync<TraceListDto>("/api/v1/traces?limit=1");
        var second = await admin.GetFromJsonAsync<TraceListDto>($"/api/v1/traces?limit=1&cursor={first!.NextCursor}");

        Assert.Equal("GET /2", Assert.Single(first.Items).Name);
        Assert.Equal("GET /1", Assert.Single(second!.Items).Name);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task GetDurations_AndTheSlowFilter_UseThe95thPercentile()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            [.. Enumerable.Range(1, 37).Select(i => Root(i, now.AddMinutes(-40).AddSeconds(i), 100)),
             Root(38, now.AddMinutes(-4), 2500),
             Root(39, now.AddMinutes(-3), 2800),
             Root(40, now.AddMinutes(-2), 3000)]);

        var durations = await admin.GetFromJsonAsync<DurationsDto>("/api/v1/traces/durations");
        var slow = await admin.GetFromJsonAsync<TraceListDto>("/api/v1/traces?slow=true");

        Assert.Equal((40, 2500d), (durations!.Read, durations.P95Ms!.Value));
        Assert.Equal(40, durations.Points.Count);
        // The 38th of 40 durations is the 95th percentile, so the three slowest are slow.
        Assert.Equal(["GET /40", "GET /39", "GET /38"], slow!.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task GetServices_ListsTheServicesOfTheWindowWithTheirTraces()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Root(1, now.AddMinutes(-3), 10),
            Root(2, now.AddMinutes(-2), 10),
            Root(3, now.AddMinutes(-1), 10) with { ServiceName = "orders" });

        var services = await admin.GetFromJsonAsync<List<ServiceDto>>("/api/v1/traces/services");

        Assert.Equal([new ServiceDto("payments", 2, 10, 0), new ServiceDto("orders", 1, 10, 0)], services);
    }

    [Fact]
    public async Task GetServices_Slowest_OrdersByThe95thPercentileWithFailures()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Root(1, now.AddMinutes(-3), 10),
            Root(2, now.AddMinutes(-2), 30),
            Root(3, now.AddMinutes(-1), 400) with { ServiceName = "orders", Status = SpanStatus.Error });

        var services = await admin.GetFromJsonAsync<List<ServiceDto>>("/api/v1/traces/services?order=slowest");

        Assert.Equal([new ServiceDto("orders", 1, 400, 1), new ServiceDto("payments", 2, 30, 0)], services);
    }

    [Fact]
    public async Task GetTraces_Worst_ListsFailedThenSlowestAndPagesWithACursor()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Root(1, now.AddMinutes(-4), 900),
            Root(2, now.AddMinutes(-3), 50) with { Status = SpanStatus.Error },
            Root(3, now.AddMinutes(-2), 300),
            Root(4, now.AddMinutes(-1), 20));

        var first = await admin.GetFromJsonAsync<TraceListDto>("/api/v1/traces?order=worst&limit=2");
        var second = await admin.GetFromJsonAsync<TraceListDto>(
            $"/api/v1/traces?order=worst&limit=2&cursor={first!.NextCursor}");
        using var mixed = await admin.GetAsync(
            new Uri($"/api/v1/traces?limit=2&cursor={first.NextCursor}", UriKind.Relative));

        Assert.Equal(["GET /2", "GET /1"], first.Items.Select(item => item.Name));
        Assert.Equal(["GET /3", "GET /4"], second!.Items.Select(item => item.Name));
        Assert.Null(second.NextCursor);
        // A cursor of one order does not continue the other.
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/traces?order=sideways")]
    [InlineData("/api/v1/traces/services?order=sideways")]
    public async Task Get_UnknownOrder_IsAProblem(string path)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetSummary_FiltersAndPeriodBefore_CountsTheRangeOnTheServer()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var to = DateTimeOffset.UtcNow;
        var from = to.AddMinutes(-30);
        await WriteAsync(
            factory,
            [.. Enumerable.Range(1, 19).Select(i => Root(i, from.AddMinutes(1).AddSeconds(i), i * 10)),
             Root(20, from.AddMinutes(2), 1000) with { Status = SpanStatus.Error },
             Root(21, from.AddMinutes(3), 70) with { ServiceName = "orders" },
             Root(22, from.AddMinutes(-10), 70),
             Root(23, from.AddMinutes(-20), 70) with { Environment = "staging" }]);
        var range = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

        var all = await admin.GetFromJsonAsync<SummaryFiguresDto>($"/api/v1/traces/summary?{range}");
        var production = await admin.GetFromJsonAsync<SummaryFiguresDto>(
            $"/api/v1/traces/summary?{range}&environment=production&service=payments");

        Assert.Equal(new SummaryFiguresDto(21, 1, 2, 100, 190, 2, 2), all);
        Assert.Equal(new SummaryFiguresDto(20, 1, 2, 100, 190, 1, 1), production);
    }

    [Fact]
    public async Task GetSummary_InvalidWindow_IsAProblem()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(
            "/api/v1/traces/summary?from=2026-11-26T12:00:00Z&to=2026-11-26T11:00:00Z",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("00000000000000000000000000000001", HttpStatusCode.NotFound)]
    [InlineData("not-a-trace-id", HttpStatusCode.BadRequest)]
    public async Task GetTrace_UnknownOrMalformedId_SaysSo(string id, HttpStatusCode expected)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri($"/api/v1/traces/{id}", UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }

    private static async Task<TraceListDto> WaitForTracesAsync(HttpClient admin, string path)
    {
        // Spans are written by a background writer a moment after they are accepted.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var list = await admin.GetFromJsonAsync<TraceListDto>(path);

            if (list!.Items.Count > 0 && list.Items[0].Spans >= 3)
            {
                return list;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The trace was not stored.");
    }

    private static async Task<string> CreateKeyAsync(HttpClient admin)
    {
        using var response = await admin.PostAsJsonAsync("/api/v1/ingestion-keys", new { name = "traces" });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedKey>())!.Secret;
    }

    /// <summary>
    /// A checkout that failed in psp-client, as OTLP/JSON: three services, upper-case hex IDs, nanosecond times as
    /// strings, and an exception event.
    /// </summary>
    private static string CheckoutJson(DateTimeOffset start)
    {
        const string trace = "5B8EFFF798038103D269B633813FC60C";

        static string Nanos(DateTimeOffset time, int milliseconds = 0) =>
            ((time.AddMilliseconds(milliseconds) - DateTimeOffset.UnixEpoch).Ticks * 100)
                .ToString(CultureInfo.InvariantCulture);

        static object Text(string key, string value) => new { key, value = new { stringValue = value } };

        static object Resource(string service, object span) => new
        {
            resource = new
            {
                attributes = new[]
                {
                    Text("service.name", service),
                    Text("deployment.environment.name", "production"),
                },
            },
            scopeSpans = new[] { new { spans = new[] { span } } },
        };

        return JsonSerializer.Serialize(new
        {
            resourceSpans = new[]
            {
                Resource("checkout-web", new
                {
                    traceId = trace,
                    spanId = "0000000000000001",
                    name = "POST /api/checkout",
                    kind = 2,
                    startTimeUnixNano = Nanos(start),
                    endTimeUnixNano = Nanos(start, 1840),
                    status = new { code = 2, message = "502" },
                }),
                Resource("psp-client", new
                {
                    traceId = trace,
                    spanId = "0000000000000002",
                    parentSpanId = "0000000000000001",
                    name = "POST /authorize",
                    kind = 2,
                    startTimeUnixNano = Nanos(start, 100),
                    endTimeUnixNano = Nanos(start, 1700),
                    status = new { code = 2 },
                    events = new[]
                    {
                        new
                        {
                            name = "exception",
                            timeUnixNano = Nanos(start, 1690),
                            attributes = new[]
                            {
                                Text("exception.type", "System.TimeoutException"),
                                Text("exception.message", "The PSP did not answer"),
                            },
                        },
                    },
                }),
                Resource("postgres", new
                {
                    traceId = trace,
                    spanId = "0000000000000003",
                    parentSpanId = "0000000000000001",
                    name = "SELECT orders",
                    kind = 3,
                    startTimeUnixNano = Nanos(start, 10),
                    endTimeUnixNano = Nanos(start, 20),
                }),
            },
        });
    }

    private static Task WriteAsync(VeyraApiFactory factory, params Span[] spans) =>
        factory.Services.GetRequiredService<ISpanWriter>().WriteAsync(spans, CancellationToken.None);

    private static Span Root(int number, DateTimeOffset start, int milliseconds) => new(
        number.ToString("x32", CultureInfo.InvariantCulture),
        number.ToString("x16", CultureInfo.InvariantCulture),
        null,
        $"GET /{number}",
        SpanKind.Server,
        start,
        TimeSpan.FromMilliseconds(milliseconds),
        SpanStatus.Unset,
        null,
        "payments",
        "production",
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        [],
        [],
        SpanDropped.None);

    private sealed record CreatedKey(string Secret);

    private sealed record TraceListDto(List<TraceItemDto> Items, string? NextCursor);

    private sealed record TraceItemDto(
        string TraceId,
        string Name,
        string Service,
        double DurationMs,
        bool Failed,
        int Spans,
        int FailedSpans,
        CauseDto? Cause,
        List<ShareDto> Services);

    private sealed record CauseDto(string Service, string SpanName, string Message);

    private sealed record ShareDto(string Service, int Spans);

    private sealed record DurationsDto(List<PointDto> Points, int Read, double? P95Ms);

    private sealed record PointDto(DateTimeOffset Start, double DurationMs, bool Failed);

    private sealed record ServiceDto(string Service, int Traces, double P95Ms, int Failed);

    private sealed record SummaryFiguresDto(
        int Traces,
        int Failed,
        int Slow,
        double? P50Ms,
        double? P95Ms,
        int Services,
        int? PreviousTraces);

    private sealed record TraceDto(string TraceId, List<SpanDto> Spans, SummaryDto Summary);

    private sealed record SummaryDto(int Spans, int Failed, double DurationMs, string? Cause);

    private sealed record SpanDto(
        string SpanId,
        string? ParentSpanId,
        string Name,
        string Kind,
        string Status,
        string Service,
        List<EventDto> Events);

    private sealed record EventDto(string Name, Dictionary<string, string> Attributes);
}
