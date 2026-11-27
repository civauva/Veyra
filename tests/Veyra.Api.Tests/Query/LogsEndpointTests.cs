using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Api.Tests.Query;

[Collection(PostgresGroup.Name)]
public class LogsEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GetLogs_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri("/api/v1/logs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLogs_WithFilters_ReturnsMatchingRecordsNewestFirst()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Record(now.AddMinutes(-3), "payment declined", LogSeverity.Error),
            Record(now.AddMinutes(-2), "payment accepted", LogSeverity.Information),
            Record(now.AddMinutes(-1), "payment retried", LogSeverity.Warning));

        var page = await admin.GetFromJsonAsync<LogsPage>("/api/v1/logs?severity=Warning&text=payment");

        Assert.Equal(["payment retried", "payment declined"], page!.Items.Select(item => item.Body));
        Assert.Equal("Warning", page.Items[0].Severity);
        Assert.Equal("payments", page.Items[0].ServiceName);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetLogs_SmallLimit_ReturnsACursorForTheNextPage()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(factory, Record(now.AddMinutes(-2), "older"), Record(now.AddMinutes(-1), "newer"));

        var first = await admin.GetFromJsonAsync<LogsPage>("/api/v1/logs?limit=1");
        var second = await admin.GetFromJsonAsync<LogsPage>($"/api/v1/logs?limit=1&cursor={first!.NextCursor}");

        Assert.Equal("newer", Assert.Single(first.Items).Body);
        Assert.Equal("older", Assert.Single(second!.Items).Body);
    }

    [Fact]
    public async Task GetLogs_AttributeFilter_ReturnsOnlyMatchingRecords()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Record(now.AddMinutes(-2), "pay") with
            {
                Attributes = new Dictionary<string, string> { ["http.route"] = "/pay" },
            },
            Record(now.AddMinutes(-1), "other"));

        var page = await admin.GetFromJsonAsync<LogsPage>(
            "/api/v1/logs?attribute=http.route:/pay&environment=production");

        Assert.Equal("pay", Assert.Single(page!.Items).Body);
    }

    [Fact]
    public async Task Get_SpanId_ReturnsOnlyThatSpansRecords()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        const string trace = "4bf92f3577b34da6a3ce929d0e0e4736";
        await WriteAsync(
            factory,
            Record(now.AddMinutes(-2), "chosen") with { TraceId = trace, SpanId = "00f067aa0ba902b7" },
            Record(now.AddMinutes(-1), "other span") with { TraceId = trace, SpanId = "53995c3f42cd8ad8" });

        var page = await admin.GetFromJsonAsync<LogsPage>($"/api/v1/logs?traceId={trace}&spanId=00f067aa0ba902b7");

        Assert.Equal("chosen", Assert.Single(page!.Items).Body);
    }

    [Fact]
    public async Task GetSummary_OneHour_ReturnsTwoMinuteBucketsOnTheClockAndServiceCounts()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var to = DateTimeOffset.UtcNow;
        var from = to.AddHours(-1);
        await WriteAsync(factory, Record(to.AddMinutes(-5), "a", LogSeverity.Error), Record(to.AddMinutes(-5), "b"));

        var summary = await admin.GetFromJsonAsync<Summary>(
            $"/api/v1/logs/summary?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");

        // The first bucket starts with the window; the others on even minutes, so they can be read from the counts.
        Assert.Equal(120, summary!.BucketSeconds);
        Assert.Equal(from, summary.Buckets[0].Start, TimeSpan.FromMilliseconds(1));
        Assert.All(
            summary.Buckets.Skip(1),
            bucket => Assert.Equal(0, bucket.Start.UtcTicks % TimeSpan.FromMinutes(2).Ticks));
        Assert.InRange(summary.Buckets.Count, 30, 31);
        Assert.Equal(2, summary.Buckets.Sum(bucket => bucket.Total));
        Assert.Equal(1, summary.Buckets.Sum(bucket => bucket.Errors));
        Assert.Equal((2L, 1L), (summary.Total, summary.Errors));
        Assert.Equal("payments", Assert.Single(summary.Services).Name);
    }

    [Theory]
    [InlineData("/api/v1/logs/summary?attribute=broken")]
    [InlineData("/api/v1/logs?attribute=no-separator")]
    [InlineData("/api/v1/logs?attribute=:value-without-key")]
    [InlineData("/api/v1/logs?cursor=garbage")]
    [InlineData("/api/v1/logs?from=2026-11-09T12:00:00Z&to=2026-11-09T11:00:00Z")]
    [InlineData("/api/v1/logs?limit=0")]
    [InlineData("/api/v1/logs?order=sideways")]
    [InlineData("/api/v1/logs?order=oldest&text=timeout")]
    public async Task GetLogs_InvalidParameters_ReturnsBadRequest(string url)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLogs_Query_FiltersAndAndsWithTheParameters()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Record(now.AddMinutes(-3), "request timed out", LogSeverity.Error) with { HostName = "web-1" },
            Record(now.AddMinutes(-2), "request timed out", LogSeverity.Information) with { HostName = "web-1" },
            Record(now.AddMinutes(-1), "out of time, timed", LogSeverity.Error) with { HostName = "web-1" });
        var q = Uri.EscapeDataString("service:payments AND level>=warning \"timed out\"");

        var page = await admin.GetFromJsonAsync<LogsPage>($"/api/v1/logs?q={q}");
        var none = await admin.GetFromJsonAsync<LogsPage>($"/api/v1/logs?q={q}&service=orders");

        var item = Assert.Single(page!.Items);
        Assert.Equal(("request timed out", "web-1"), (item.Body, item.HostName));
        Assert.Empty(none!.Items);
    }

    [Fact]
    public async Task GetLogs_HostAndLevel_TakeAnyOfTheirValues()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Record(now.AddMinutes(-4), "a", LogSeverity.Warning) with { HostName = "web-1" },
            Record(now.AddMinutes(-3), "b", LogSeverity.Error) with { HostName = "web-2" },
            Record(now.AddMinutes(-2), "c", LogSeverity.Fatal) with { HostName = "web-3" },
            Record(now.AddMinutes(-1), "d", LogSeverity.Information) with { HostName = "web-1" });

        var page = await admin.GetFromJsonAsync<LogsPage>(
            "/api/v1/logs?host=web-1&host=web-2&level=Warning&level=Error");

        Assert.Equal(["b", "a"], page!.Items.Select(item => item.Body));
    }

    [Theory]
    [InlineData("/api/v1/logs", "service:", 8, "a value")]
    [InlineData("/api/v1/logs/summary", "time:1h", 0, "a term without time")]
    [InlineData("/api/v1/logs/export", "\"open", 5, "a closing quote")]
    [InlineData("/api/v1/logs/stream", "a OR b", 2, "AND")]
    [InlineData("/api/v1/logs/parse", "level>=loud", 7, "a level")]
    public async Task GetLogs_BrokenQuery_IsAProblemWithItsPlace(string path, string q, int position, string expected)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri($"{path}?q={Uri.EscapeDataString(q)}", UriKind.Relative));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(position, problem.GetProperty("position").GetInt32());
        Assert.Equal(expected, problem.GetProperty("expected").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task GetParse_Query_AnswersEachTermWithItsPlace()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var q = Uri.EscapeDataString("service:\"Orders API\" AND level>=warn http.route:/pay \"timed out\"");

        var parse = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/logs/parse?q={q}");

        var terms = parse.GetProperty("terms").EnumerateArray().ToList();
        Assert.Equal(
            [
                "service : Orders API 0 20 ",
                "level >= warn 25 36 ",
                "attribute : /pay 37 52 http.route",
                "text  timed out 53 64 ",
            ],
            terms.Select(term =>
                $"{term.GetProperty("field").GetString()} {term.GetProperty("op").GetString()} "
                + $"{term.GetProperty("value").GetString()} {term.GetProperty("start").GetInt32()} "
                + $"{term.GetProperty("end").GetInt32()} {term.GetProperty("key").GetString()}"));
    }

    [Fact]
    public async Task GetSummary_Levels_AreCountedPerBucketAndForTheWindow()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var to = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Record(to.AddMinutes(-5), "a", LogSeverity.Error),
            Record(to.AddMinutes(-5), "b"),
            Record(to.AddMinutes(-5), "c"),
            Record(to.AddMinutes(-4), "d", LogSeverity.Warning) with { HostName = "web-1" });

        var summary = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/logs/summary?from={Escape(to.AddHours(-1))}&to={Escape(to)}");
        var byHost = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/logs/summary?from={Escape(to.AddHours(-1))}&to={Escape(to)}&q=host:web-1");

        var levels = summary.GetProperty("levels");
        Assert.Equal(
            (2, 1, 1),
            (levels.GetProperty("Information").GetInt64(),
                levels.GetProperty("Warning").GetInt64(),
                levels.GetProperty("Error").GetInt64()));
        Assert.Equal(
            4,
            summary.GetProperty("buckets").EnumerateArray()
                .SelectMany(bucket => bucket.GetProperty("levels").EnumerateObject())
                .Sum(level => level.Value.GetInt64()));
        Assert.Equal(4, summary.GetProperty("total").GetInt64());
        Assert.Equal(1, byHost.GetProperty("total").GetInt64());
        Assert.Equal(1, byHost.GetProperty("levels").GetProperty("Warning").GetInt64());
    }

    [Fact]
    public async Task GetExport_Ndjson_WritesEveryRecordNewestFirstAsAnAttachment()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Record(now.AddMinutes(-3), "first"),
            Record(now.AddMinutes(-2), "second") with { HostName = "web-1" },
            Record(now.AddMinutes(-1), "other", LogSeverity.Error));

        using var response = await admin.GetAsync(new Uri("/api/v1/logs/export?level=Information", UriKind.Relative));
        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Matches(@"^veyra-logs-\d{8}T\d{6}Z\.ndjson$", disposition.FileName);
        Assert.Equal(
            ["second", "first"],
            lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("body").GetString()));
        Assert.Equal("web-1", JsonDocument.Parse(lines[0]).RootElement.GetProperty("hostName").GetString());
    }

    [Fact]
    public async Task GetExport_MoreThanTheLimit_StopsThereAndSaysSo()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres, ("Veyra:Query:MaxExportRecords", "2"));
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteAsync(
            factory,
            Record(now.AddMinutes(-3), "first"),
            Record(now.AddMinutes(-2), "second"),
            Record(now.AddMinutes(-1), "third"));

        var ndjson = (await admin.GetStringAsync(new Uri("/api/v1/logs/export", UriKind.Relative)))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var csv = (await admin.GetStringAsync(new Uri("/api/v1/logs/export?format=csv", UriKind.Relative)))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var exact = (await admin.GetStringAsync(
                new Uri($"/api/v1/logs/export?from={Escape(now.AddSeconds(-150))}", UriKind.Relative)))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, ndjson.Length);
        Assert.Equal("{\"truncated\":true,\"limit\":2}", ndjson[^1]);
        Assert.Equal(4, csv.Length);
        Assert.StartsWith("timestamp,", csv[0], StringComparison.Ordinal);
        Assert.Contains(",third,", csv[1], StringComparison.Ordinal);
        Assert.Equal("# truncated at 2 records", csv[^1]);
        Assert.Equal(2, exact.Length);
    }

    private static string Escape(DateTimeOffset time) => Uri.EscapeDataString(time.ToString("O"));

    private static Task WriteAsync(VeyraApiFactory factory, params Core.Telemetry.LogRecord[] records) =>
        factory.Services.GetRequiredService<ILogWriter>().WriteAsync(records, CancellationToken.None);

    private static Core.Telemetry.LogRecord Record(
        DateTimeOffset timestamp,
        string body,
        LogSeverity severity = LogSeverity.Information) => new(
        timestamp,
        timestamp,
        severity,
        severity.ToString(),
        body,
        "payments",
        "production",
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = "payments" });

    private sealed record Summary(
        int BucketSeconds,
        List<Bucket> Buckets,
        List<Service> Services,
        long Total,
        long Errors);

    private sealed record Bucket(DateTimeOffset Start, long Total, long Errors);

    private sealed record Service(string Name, long Count);

    private sealed record LogsPage(List<LogEntry> Items, string? NextCursor);

    private sealed record LogEntry(
        long Id,
        DateTimeOffset Timestamp,
        string Severity,
        string Body,
        string ServiceName,
        string? HostName = null);
}
