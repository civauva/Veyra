using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Alerting.ErrorRates;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Api.Tests.Settings;

/// <summary>The Admin's "Clear all telemetry" on the Settings screen (VEY-59).</summary>
[Collection(PostgresGroup.Name)]
public class ClearTelemetryEndpointTests(PostgresFixture postgres)
{
    private const string _password = "a-long-enough-password";

    private const string _path = "/api/v1/settings/clear-telemetry";

    [Fact]
    public async Task Clear_Admin_DeletesLogsMetricsAndAlerts_AndIngestionCarriesOn()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var logs = factory.Services.GetRequiredService<ILogWriter>();
        var metrics = factory.Services.GetRequiredService<IMetricWriter>();
        await logs.WriteAsync(Records("payments-gateway"), CancellationToken.None);
        await metrics.WriteAsync([Point(1)], CancellationToken.None);
        await factory.Services.GetRequiredService<ErrorRateEvaluator>().EvaluateAsync(CancellationToken.None);
        Assert.NotEmpty((await admin.GetFromJsonAsync<AlertList>("/api/v1/alerts"))!.Items);

        using var response = await admin.PostAsJsonAsync(_path, new { confirm = admin.BaseAddress!.Host });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<AlertList>("/api/v1/alerts"))!.Items);
        Assert.Empty(await SeriesAsync(factory));
        Assert.Empty((await admin.GetFromJsonAsync<LogPage>("/api/v1/logs"))!.Items);

        // The same series again: the store must not point at the deleted series ID it had cached.
        var written = await metrics.WriteAsync([Point(2)], CancellationToken.None);

        Assert.Equal(1, written.Stored);
        Assert.Single(await SeriesAsync(factory));
    }

    [Fact]
    public async Task Clear_WrongConfirmation_ReturnsBadRequestAndKeepsEverything()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await factory.Services.GetRequiredService<IMetricWriter>().WriteAsync([Point(1)], CancellationToken.None);

        using var response = await admin.PostAsJsonAsync(_path, new { confirm = "yes" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(await SeriesAsync(factory));
    }

    [Fact]
    public async Task Clear_Editor_ReturnsForbidden()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var created = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "editor@example.com", displayName = "Editor", role = "Editor", password = _password });
        created.EnsureSuccessStatusCode();
        using var editor = await factory.CreateSignedInClientAsync("editor@example.com", _password);

        using var response = await editor.PostAsJsonAsync(_path, new { confirm = editor.BaseAddress!.Host });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Task<IReadOnlyList<MetricSeries>> SeriesAsync(VeyraApiFactory factory) =>
        factory.Services.GetRequiredService<IMetricSearch>().FindSeriesAsync(
            new MetricSeriesQuery(DateTimeOffset.UtcNow.AddDays(-1), Names: ["veyra.test.gauge"]),
            CancellationToken.None);

    private static MetricPoint Point(double value) =>
        new(
            "veyra.test.gauge",
            MetricKind.Gauge,
            "1",
            "A gauge for the test.",
            MetricTemporality.Unspecified,
            false,
            "payments-gateway",
            "production",
            null,
            new Dictionary<string, string> { ["service.name"] = "payments-gateway" },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            null,
            value,
            null);

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

    private sealed record AlertList(IReadOnlyList<object> Items);

    private sealed record LogPage(IReadOnlyList<object> Items);
}
