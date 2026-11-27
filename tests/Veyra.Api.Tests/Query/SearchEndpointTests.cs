using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Search;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Query.Search;
using Veyra.Testing;
using Xunit.Abstractions;

namespace Veyra.Api.Tests.Query;

[Collection(PostgresGroup.Name)]
public class SearchEndpointTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [Fact]
    public async Task GetSearch_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri("/api/v1/search?q=pay", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSearch_StoredSignals_FindsEveryGroupRankedOnTheServer()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);

        var found = await admin.GetFromJsonAsync<JsonElement>("/api/v1/search?q=pay");

        Assert.Equal("pay", found.GetProperty("query").GetString());
        Assert.Equal(["pay", "Payments.API", "old-payments"], Names(found, "services"));
        Assert.Equal("Ok", Item(found, "services", 0).GetProperty("health").GetString());
        Assert.Equal("3 logs", Item(found, "services", 2).GetProperty("detail").GetString());
        Assert.Equal(["payhost-01", "payments-5c2"], Names(found, "hosts"));
        Assert.Equal(["host", "container"], Kinds(found, "hosts"));
        Assert.Equal(["PaymentsDB", "PAYSQL-01"], Names(found, "databases"));
        var database = Item(found, "databases", 0);
        Assert.Equal("database", database.GetProperty("kind").GetString());
        Assert.Equal("microsoft.sql_server", database.GetProperty("engine").GetString());
        Assert.Equal("SQL-PROD-01", database.GetProperty("server").GetString());
        Assert.Equal("22 GB", database.GetProperty("detail").GetString());
        Assert.False(Item(found, "services", 0).TryGetProperty("engine", out _));
        var operation = Item(found, "traces", 0);
        Assert.Equal("POST /payments", operation.GetProperty("name").GetString());
        Assert.Equal("Payments.API", operation.GetProperty("service").GetString());
        Assert.Equal(3, operation.GetProperty("count").GetInt64());
        Assert.Equal("3 in 24 h, 1 failed", operation.GetProperty("detail").GetString());
        Assert.Single(found.GetProperty("traces").GetProperty("items").EnumerateArray());
        Assert.All(
            new[] { "services", "hosts", "databases", "traces", "logs" },
            group => Assert.False(found.GetProperty(group).GetProperty("partial").GetBoolean()));
    }

    [Fact]
    public async Task GetSearch_Words_CountsTheMatchingLogRecordsOfTheWindow()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteLogsAsync(
            factory,
            Log(now.AddMinutes(-5), "payment declined", "billing"),
            Log(now.AddHours(-5), "payment declined again", "billing"),
            Log(now.AddDays(-2), "payment declined long ago", "billing"),
            Log(now.AddMinutes(-1), "payment accepted", "billing"),
            Log(now.AddMinutes(-1), "payment declined", "billing", "staging"));

        var all = await admin.GetFromJsonAsync<JsonElement>("/api/v1/search?q=declined%20payment");
        var production = await admin.GetFromJsonAsync<JsonElement>(
            "/api/v1/search?q=declined%20payment&environment=production");

        Assert.Equal(3, all.GetProperty("logs").GetProperty("count").GetInt64());
        Assert.Equal(2, production.GetProperty("logs").GetProperty("count").GetInt64());
    }

    [Fact]
    public async Task GetSearch_Environment_KeepsServicesOfThatEnvironmentOnly()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await WriteLogsAsync(
            factory,
            Log(now.AddMinutes(-5), "x", "payments-prod"),
            Log(now.AddMinutes(-5), "x", "payments-stage", "staging"));

        var found = await admin.GetFromJsonAsync<JsonElement>("/api/v1/search?q=payments&environment=staging");

        Assert.Equal(["payments-stage"], Names(found, "services"));
    }

    [Fact]
    public async Task GetSearch_EmptyText_AnswersEveryGroupEmpty()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);

        var found = await admin.GetFromJsonAsync<JsonElement>("/api/v1/search?q=%20%20");

        Assert.Empty(Names(found, "services"));
        Assert.Equal(0, found.GetProperty("logs").GetProperty("count").GetInt64());
    }

    [Fact]
    public async Task GetSearch_TextWithWildcards_MatchesThemLiterally()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);

        var found = await admin.GetFromJsonAsync<JsonElement>("/api/v1/search?q=%25");

        Assert.Empty(Names(found, "services"));
        Assert.Empty(Names(found, "hosts"));
    }

    [Fact]
    public async Task GetSearch_NoTimeLeft_MarksEveryGroupPartialAndStillAnswers()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var slow = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton(new SearchBudget(TimeSpan.Zero))));
        await SeedAsync(factory);
        using var admin = await SignedInAsync(factory, slow);

        using var response = await admin.GetAsync(new Uri("/api/v1/search?q=pay", UriKind.Relative));
        var found = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(
            new[] { "services", "hosts", "databases", "traces", "logs" },
            group => Assert.True(found.GetProperty(group).GetProperty("partial").GetBoolean()));
    }

    [Theory]
    [InlineData("/api/v1/search?q=pay&from=2026-11-09T12:00:00Z&to=2026-11-09T11:00:00Z")]
    [InlineData("/api/v1/search?q=pay&from=2026-09-01T00:00:00Z&to=2026-11-09T00:00:00Z")]
    public async Task GetSearch_InvalidWindow_ReturnsBadRequest(string url)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SearchAsync_TestData_EachGroupAnswersWithinItsBudget()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        await SeedAsync(factory);
        var signals = factory.Services.GetRequiredService<ISignalSearch>();
        var logs = factory.Services.GetRequiredService<ILogSearch>();
        var now = DateTimeOffset.UtcNow;
        var search = new NameSearch("pay", now.AddDays(-1), now, null, 5);

        // Warm up the connections and plans, as a second search does.
        await factory.Services.GetRequiredService<SearchBuilder>()
            .SearchAsync("pay", null, now.AddDays(-1), now, CancellationToken.None);

        var services = await TimeAsync(() => signals.FindBlocksAsync(search, [AlertBlockKind.Service], default));
        var hosts = await TimeAsync(() => signals.FindBlocksAsync(
            search,
            [AlertBlockKind.Host, AlertBlockKind.Container],
            default));
        var databases = await TimeAsync(() => signals.FindBlocksAsync(search, [AlertBlockKind.Database], default));
        var traces = await TimeAsync(() => signals.FindOperationsAsync(search, default));
        var words = await TimeAsync(() => logs.SummarizeAsync(
            new LogQuery(now.AddDays(-1), now, null, null, "declined", null, 1, null),
            TimeSpan.FromDays(1),
            default));
        var whole = await TimeAsync(() => factory.Services.GetRequiredService<SearchBuilder>()
            .SearchAsync("pay", null, now.AddDays(-1), now, CancellationToken.None));

        output.WriteLine(
            $"services {services} ms, hosts {hosts} ms, databases {databases} ms, traces {traces} ms, "
            + $"logs {words} ms, whole search {whole} ms");
        Assert.All(new[] { services, hosts, databases, traces, words }, time => Assert.InRange(time, 0, 150));
    }

    private static async Task<double> TimeAsync<T>(Func<Task<T>> read)
    {
        var started = Stopwatch.GetTimestamp();
        await read();

        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static async Task<HttpClient> SignedInAsync(VeyraApiFactory factory, WebApplicationFactory<Program> slow)
    {
        // Setup runs once per database, so the first factory creates the admin and the second signs in as it.
        (await factory.CreateAdminClientAsync()).Dispose();

        var client = slow.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        using var signIn = await client.PostAsJsonAsync(
            "/api/v1/session",
            new { email = VeyraApiFactory.AdminEmail, password = VeyraApiFactory.AdminPassword });
        signIn.EnsureSuccessStatusCode();

        return client;
    }

    private static async Task SeedAsync(VeyraApiFactory factory)
    {
        var now = DateTimeOffset.UtcNow;

        await WriteLogsAsync(
            factory,
            Log(now.AddMinutes(-5), "payment declined", "old-payments"),
            Log(now.AddMinutes(-4), "payment declined", "old-payments"),
            Log(now.AddMinutes(-3), "payment declined", "old-payments"),
            Log(now.AddMinutes(-3), "ready", "Payments.API"),
            Log(now.AddMinutes(-3), "ready", "pay"),
            Log(now.AddMinutes(-3), "ready", "orders"));

        var sqlServer = TelemetryBlock.Database("microsoft.sql_server", "SQL-PROD-01");
        await factory.Services.GetRequiredService<IMetricWriter>().WriteAsync(
            [
                Point("system.cpu.time", new TelemetryBlock(AlertBlockKind.Host, "payhost-01"), now, 1),
                Point(
                    "container.cpu.utilization",
                    new TelemetryBlock(AlertBlockKind.Container, "payments-5c2"),
                    now,
                    1),
                Point("container.cpu.utilization", new TelemetryBlock(AlertBlockKind.Container, "orders-1"), now, 1),
                Point(
                    "sqlserver.database.size",
                    sqlServer,
                    now.AddMinutes(-2),
                    22d * (1L << 30),
                    new Dictionary<string, string> { ["database_name"] = "PaymentsDB" }),
                Point(
                    "sqlserver.batch.request.rate",
                    TelemetryBlock.Database("microsoft.sql_server", "PAYSQL-01"),
                    now,
                    5),
            ],
            CancellationToken.None);

        await factory.Services.GetRequiredService<ISpanWriter>().WriteAsync(
            [
                Root("POST /payments", "Payments.API", now.AddMinutes(-10), SpanStatus.Ok),
                Root("POST /payments", "Payments.API", now.AddHours(-3), SpanStatus.Error),
                Root("POST /payments", "Payments.API", now.AddHours(-20), SpanStatus.Ok),
                Root("POST /payments", "Payments.API", now.AddDays(-3), SpanStatus.Ok),
                Root("GET /orders", "orders", now.AddMinutes(-10), SpanStatus.Ok),
            ],
            CancellationToken.None);
    }

    private static Task WriteLogsAsync(VeyraApiFactory factory, params Core.Telemetry.LogRecord[] records) =>
        factory.Services.GetRequiredService<ILogWriter>().WriteAsync(records, CancellationToken.None);

    private static Core.Telemetry.LogRecord Log(
        DateTimeOffset timestamp,
        string body,
        string service,
        string environment = "production") => new(
        timestamp,
        timestamp,
        LogSeverity.Information,
        "Information",
        body,
        service,
        environment,
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = service });

    private static MetricPoint Point(
        string name,
        TelemetryBlock block,
        DateTimeOffset at,
        double value,
        Dictionary<string, string>? attributes = null) => new(
        name,
        MetricKind.Gauge,
        "1",
        "",
        MetricTemporality.Unspecified,
        false,
        null,
        null,
        block.Kind == AlertBlockKind.Host ? block.Name : null,
        new Dictionary<string, string>(),
        attributes ?? new Dictionary<string, string>(),
        at,
        null,
        value,
        null,
        block);

    private static Span Root(string name, string service, DateTimeOffset start, SpanStatus status) => new(
        Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()),
        Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()[..8]),
        null,
        name,
        SpanKind.Server,
        start,
        TimeSpan.FromMilliseconds(120),
        status,
        null,
        service,
        "production",
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = service },
        [],
        [],
        SpanDropped.None);

    private static JsonElement Item(JsonElement found, string group, int index) =>
        found.GetProperty(group).GetProperty("items")[index];

    private static List<string?> Names(JsonElement found, string group) =>
        found.GetProperty(group).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToList();

    private static List<string?> Kinds(JsonElement found, string group) =>
        found.GetProperty(group).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString())
            .ToList();
}
