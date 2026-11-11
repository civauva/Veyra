using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Alerting.ErrorRates;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Api.Tests.Query;

[Collection(PostgresGroup.Name)]
public class HomeEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GetHome_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri("/api/v1/home", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHome_HealthyServices_IsClearWithBlocksAndMetrics()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await WriteAsync(factory, Records("orders", total: 30, errors: 0));

        var home = await admin.GetFromJsonAsync<Home>("/api/v1/home?environment=production");

        Assert.Equal("production", home!.Environment);
        Assert.Equal("Clear", home.Condition.Kind);
        Assert.Equal(0, home.Condition.ErrorRatio);
        var block = Assert.Single(home.Blocks);
        Assert.Equal("service:production/orders", block.Id);
        Assert.Equal("Ok", block.Health);
        Assert.Empty(home.Alerts);
        Assert.Equal(24, home.Hourly.Observed.Count);
        Assert.True(home.Storage.UsedBytes > 0);
    }

    [Fact]
    public async Task GetHome_AfterTheErrorRateRuleFires_IsStormWithNumberedAlert()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await WriteAsync(factory, Records("payments-gateway", total: 40, errors: 10));
        await WriteAsync(factory, Records("orders", total: 40, errors: 0));

        await factory.Services.GetRequiredService<ErrorRateEvaluator>().EvaluateAsync(CancellationToken.None);
        var home = await admin.GetFromJsonAsync<Home>("/api/v1/home");

        Assert.Equal("Storm", home!.Condition.Kind);
        Assert.Equal("Services", home.Condition.Over);
        var alert = Assert.Single(home.Alerts);
        Assert.Equal(1, alert.N);
        Assert.Equal("payments-gateway", alert.Service);
        Assert.Equal("Warning", alert.Severity);
        Assert.Equal(0.25, alert.Value);
        Assert.Equal("Warning", home.Blocks.Single(block => block.Name == "payments-gateway").Health);
        Assert.Equal(new Counts(1, 0, 1, 0), home.Counts);
        Assert.Equal(["production"], home.Environments.Select(environment => environment.Name));
    }

    private static Task WriteAsync(VeyraApiFactory factory, LogRecord[] records) =>
        factory.Services.GetRequiredService<ILogWriter>().WriteAsync(records, CancellationToken.None);

    // Spread over the last two minutes, so they fall inside the five-minute window of the rule and the snapshot.
    private static LogRecord[] Records(string service, int total, int errors)
    {
        var now = DateTimeOffset.UtcNow;

        return Enumerable.Range(0, total)
            .Select(index =>
            {
                var severity = index < errors ? LogSeverity.Error : LogSeverity.Information;
                var timestamp = now.AddSeconds(-index * 3);

                return new LogRecord(
                    timestamp,
                    timestamp,
                    severity,
                    severity.ToString(),
                    $"record {index}",
                    service,
                    "production",
                    null,
                    null,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["service.name"] = service });
            })
            .ToArray();
    }

    private sealed record Home(
        string? Environment,
        Condition Condition,
        Counts Counts,
        List<Alert> Alerts,
        List<Block> Blocks,
        List<Environment> Environments,
        Hourly Hourly,
        Storage Storage);

    private sealed record Condition(string Kind, string? Over, double? ErrorRatio);

    private sealed record Counts(int Warning, int Caution, int Ok, int Unknown);

    private sealed record Alert(int? N, string Service, string Severity, double Value);

    private sealed record Block(string Id, string Name, string Health);

    private sealed record Environment(string Name);

    private sealed record Hourly(List<LogBucket> Observed);

    private sealed record Storage(long UsedBytes);
}
