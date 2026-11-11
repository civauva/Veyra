using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.ErrorRates;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Api.Tests.Alerting;

/// <summary>The Alerts screen's endpoints and alerts about hosts (ADR 0012).</summary>
[Collection(PostgresGroup.Name)]
public class AlertHistoryEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ListAndSummary_AfterTheRuleFires_ShowTheAlert()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await factory.Services.GetRequiredService<ILogWriter>().WriteAsync(Records("payments-gateway"), CancellationToken.None);
        await factory.Services.GetRequiredService<ErrorRateEvaluator>().EvaluateAsync(CancellationToken.None);

        var list = await admin.GetFromJsonAsync<AlertList>("/api/v1/alerts");
        var summary = await admin.GetFromJsonAsync<Summary>("/api/v1/alerts/summary?days=7");

        var alert = Assert.Single(list!.Items);
        Assert.Equal(("error-rate", "Warning", "Warning"), (alert.Rule, alert.Severity, alert.Worst));
        Assert.Equal(new Block("service", "payments-gateway", null, "service:production/payments-gateway"), alert.Block);
        Assert.Null(alert.ResolvedAt);
        Assert.Null(list.Next);
        Assert.Equal((1, 1, 1, 1, 0), (summary!.Open, summary.OpenWarnings, summary.Total, summary.Warnings, summary.Resolved));
        Assert.Equal(7, summary.Days.Count);
        Assert.Equal((1, "Warning"), (summary.Days[^1].Count, summary.Days[^1].Worst));
        Assert.Equal(1, summary.Rules.Single(rule => rule.Rule == "error-rate").Count);
        Assert.Equal(4, summary.Rules.Count);
    }

    [Fact]
    public async Task HostAlert_IsListedByKindWithItsDiskDetailsAndLightsAHostBlock()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        var disk = new AlertState(
            Guid.CreateVersion7(),
            AlertRules.DiskFilling,
            "production",
            AlertSubject.Host("web-01", "/"),
            AlertSeverity.Caution,
            9.2,
            9.2,
            14,
            TimeSpan.FromDays(1),
            now.AddHours(-4),
            now,
            null);
        await factory.Services.GetRequiredService<AlertStore>().InsertAsync(disk, CancellationToken.None);

        var hosts = await admin.GetFromJsonAsync<AlertList>("/api/v1/alerts?kind=host&q=web");
        var services = await admin.GetFromJsonAsync<AlertList>("/api/v1/alerts?kind=service");
        var details = await admin.GetFromJsonAsync<Details>($"/api/v1/alerts/{disk.Id}");
        var home = await admin.GetFromJsonAsync<Home>("/api/v1/home");

        Assert.Equal(disk.Id, Assert.Single(hosts!.Items).Id);
        Assert.Empty(services!.Items);
        Assert.Equal(new Block("host", "web-01", "/", "host:production/web-01"), details!.Block);
        Assert.Null(details.Service);
        Assert.True(details.LowerIsWorse);
        Assert.Equal("/", details.Disk!.Mountpoint);
        Assert.Null(details.Disk.Ratio);
        Assert.Null(details.Series);
        var block = Assert.Single(home!.Blocks, one => one.Kind == "host");
        Assert.Equal(("host:production/web-01", "Caution"), (block.Id, block.Health));
        Assert.Equal(("host", "web-01", "/"), (home.Alerts[0].Kind, home.Alerts[0].Name, home.Alerts[0].Part));
    }

    [Theory]
    [InlineData("days=0")]
    [InlineData("days=91")]
    [InlineData("state=sideways")]
    [InlineData("rule=cpu")]
    [InlineData("kind=planet")]
    [InlineData("limit=0")]
    [InlineData("limit=201")]
    [InlineData("cursor=not-a-cursor")]
    public async Task List_InvalidQuery_IsAnsweredWithAProblem(string query)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri($"/api/v1/alerts?{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await response.Content.ReadFromJsonAsync<Problem>())!.Title));
    }

    [Fact]
    public async Task List_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var list = await anonymous.GetAsync(new Uri("/api/v1/alerts", UriKind.Relative));
        using var summary = await anonymous.GetAsync(new Uri("/api/v1/alerts/summary", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, summary.StatusCode);
    }

    // 40 records in the last minute, a quarter of them errors: a warning for the error-rate rule.
    private static LogRecord[] Records(string service)
    {
        var now = DateTimeOffset.UtcNow;

        return Enumerable.Range(0, 40)
            .Select(index =>
            {
                var severity = index % 4 == 0 ? LogSeverity.Error : LogSeverity.Information;
                var timestamp = now.AddSeconds(-index);

                return new LogRecord(
                    timestamp,
                    timestamp,
                    severity,
                    severity.ToString(),
                    "ok",
                    service,
                    "production",
                    null,
                    null,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["service.name"] = service });
            })
            .ToArray();
    }

    private sealed record AlertList(List<Item> Items, string? Next);

    private sealed record Item(Guid Id, string Rule, Block Block, string Severity, string Worst, DateTimeOffset? ResolvedAt);

    private sealed record Block(string Kind, string Name, string? Part, string Id);

    private sealed record Summary(
        int Open,
        int OpenWarnings,
        int Total,
        int Warnings,
        int Resolved,
        List<RuleCount> Rules,
        List<Day> Days);

    private sealed record RuleCount(string Rule, int Count);

    private sealed record Day(DateOnly Date, int Count, string? Worst);

    private sealed record Details(Block Block, string? Service, bool LowerIsWorse, Disk? Disk, object? Series);

    private sealed record Disk(string Mountpoint, double? Ratio);

    private sealed record Home(List<HomeAlert> Alerts, List<HomeBlock> Blocks);

    private sealed record HomeAlert(string Kind, string Name, string? Part);

    private sealed record HomeBlock(string Id, string Kind, string Health);

    private sealed record Problem(string Title);
}
