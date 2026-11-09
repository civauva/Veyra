using System.Net;
using System.Net.Http.Json;
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
    public async Task GetSummary_OneHour_ReturnsThirtyTwoMinuteBucketsAndServiceCounts()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var to = DateTimeOffset.UtcNow;
        var from = to.AddHours(-1);
        await WriteAsync(factory, Record(to.AddMinutes(-5), "a", LogSeverity.Error), Record(to.AddMinutes(-5), "b"));

        var summary = await admin.GetFromJsonAsync<Summary>(
            $"/api/v1/logs/summary?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");

        Assert.Equal(120, summary!.BucketSeconds);
        Assert.Equal(30, summary.Buckets.Count);
        Assert.Equal(2, summary.Buckets.Sum(bucket => bucket.Total));
        Assert.Equal(1, summary.Buckets.Sum(bucket => bucket.Errors));
        Assert.Equal("payments", Assert.Single(summary.Services).Name);
    }

    [Theory]
    [InlineData("/api/v1/logs/summary?attribute=broken")]
    [InlineData("/api/v1/logs?attribute=no-separator")]
    [InlineData("/api/v1/logs?attribute=:value-without-key")]
    [InlineData("/api/v1/logs?cursor=garbage")]
    [InlineData("/api/v1/logs?from=2026-11-09T12:00:00Z&to=2026-11-09T11:00:00Z")]
    [InlineData("/api/v1/logs?limit=0")]
    public async Task GetLogs_InvalidParameters_ReturnsBadRequest(string url)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

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

    private sealed record Summary(int BucketSeconds, List<Bucket> Buckets, List<Service> Services);

    private sealed record Bucket(DateTimeOffset Start, long Total, long Errors);

    private sealed record Service(string Name, long Count);

    private sealed record LogsPage(List<LogEntry> Items, string? NextCursor);

    private sealed record LogEntry(long Id, DateTimeOffset Timestamp, string Severity, string Body, string ServiceName);
}
