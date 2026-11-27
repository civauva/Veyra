using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Alerting.Alerts;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Api.Tests.Query;

/// <summary>What surrounds one alert, read by the Query module from the alert Alerting keeps (ADR 0029).</summary>
[Collection(PostgresGroup.Name)]
public class AlertContextEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Context_ServiceAlert_HasItsBlockAndItsWarningsFromAnHourBeforeItOpened()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        // Whole seconds, as PostgreSQL keeps microseconds and the records are compared by their times.
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var alert = Alert("payments-gateway", now.AddMinutes(-20));
        await factory.Services.GetRequiredService<AlertStore>().InsertAsync(alert, CancellationToken.None);
        await factory.Services.GetRequiredService<ILogWriter>().WriteAsync(
            [
                Record("payments-gateway", now.AddMinutes(-1), LogSeverity.Error),
                Record("payments-gateway", now.AddMinutes(-2), LogSeverity.Warning),
                Record("payments-gateway", now.AddMinutes(-70), LogSeverity.Error),
                Record("payments-gateway", now.AddMinutes(-3), LogSeverity.Information),
                Record("payments-gateway", now.AddMinutes(-90), LogSeverity.Error),
                Record("orders", now.AddMinutes(-1), LogSeverity.Error),
            ],
            CancellationToken.None);

        var context = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alert.Id}/context");

        Assert.Equal(
            alert.OpenedAt.AddHours(-1),
            context.GetProperty("from").GetDateTimeOffset(),
            TimeSpan.FromMilliseconds(1));
        Assert.True(context.GetProperty("to").GetDateTimeOffset() >= now);
        var logs = context.GetProperty("logs");
        // Newest first; the information record, the record before the window, and the other service are left out.
        Assert.Equal(
            [now.AddMinutes(-1), now.AddMinutes(-2), now.AddMinutes(-70)],
            logs.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("timestamp").GetDateTimeOffset()));
        Assert.Equal(3, logs.GetProperty("total").GetInt64());
        Assert.Equal(
            "service:payments-gateway AND env:production AND level>=warning",
            logs.GetProperty("query").GetString());
        var component = Assert.Single(context.GetProperty("components").EnumerateArray());
        Assert.Equal(
            ("service:production/payments-gateway", "warning", true, "affected"),
            (component.GetProperty("block").GetProperty("id").GetString(),
                component.GetProperty("health").GetString(),
                component.GetProperty("inIncident").GetBoolean(),
                component.GetProperty("relation").GetString()));
    }

    [Fact]
    public async Task Context_ResolvedAlert_EndsWhenItResolved()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        var alert = Alert("payments-gateway", now.AddHours(-3));
        var store = factory.Services.GetRequiredService<AlertStore>();
        await store.InsertAsync(alert, CancellationToken.None);
        await store.ResolveAsync(alert.Id, 0, now.AddHours(-2), CancellationToken.None);

        var context = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alert.Id}/context");

        Assert.Equal(now.AddHours(-2), context.GetProperty("to").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));
        var component = Assert.Single(context.GetProperty("components").EnumerateArray());
        Assert.Equal(
            ("ok", true),
            (component.GetProperty("health").GetString(), component.GetProperty("inIncident").GetBoolean()));
    }

    [Fact]
    public async Task Context_UnknownAlert_IsNotFound()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(
            new Uri($"/api/v1/alerts/{Guid.CreateVersion7()}/context", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Context_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(
            new Uri($"/api/v1/alerts/{Guid.CreateVersion7()}/context", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static AlertState Alert(string service, DateTimeOffset openedAt) => new(
        Guid.CreateVersion7(),
        AlertRules.ErrorRate,
        "production",
        AlertSubject.Service(service),
        AlertSeverity.Warning,
        0.2,
        0.2,
        0.05,
        TimeSpan.FromMinutes(5),
        openedAt,
        openedAt,
        null);

    private static LogRecord Record(string service, DateTimeOffset timestamp, LogSeverity severity) => new(
        timestamp,
        timestamp,
        severity,
        severity.ToString(),
        "Payment failed",
        service,
        "production",
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = service });
}
