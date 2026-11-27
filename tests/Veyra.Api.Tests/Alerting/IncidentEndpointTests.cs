using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.ErrorRates;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Testing;

namespace Veyra.Api.Tests.Alerting;

/// <summary>The incident endpoints of the Alerting module and the context of the Query module (ADR 0026).</summary>
[Collection(PostgresGroup.Name)]
public class IncidentEndpointTests(PostgresFixture postgres)
{
    private const string _password = "another long secret";

    [Fact]
    public async Task List_RelatedAlerts_AreOneIncidentWithBothBlocks()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var (disk, container) = await OpenHostIncidentAsync(factory);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/incidents?state=all&days=7&limit=10");
        var open = await admin.GetFromJsonAsync<JsonElement>("/api/v1/incidents?state=open");
        var resolved = await admin.GetFromJsonAsync<JsonElement>("/api/v1/incidents?state=resolved");

        var incident = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, list.GetProperty("next").ValueKind);
        Assert.Equal("INC-1", incident.GetProperty("key").GetString());
        Assert.Equal(1, incident.GetProperty("number").GetInt64());
        Assert.Equal("Container shop-api on web-01 uses 95% of its memory limit", incident.GetProperty("title").GetString());
        Assert.Equal(("Warning", "open", "production"), (
            incident.GetProperty("severity").GetString(),
            incident.GetProperty("state").GetString(),
            incident.GetProperty("environment").GetString()));
        Assert.Equal(2, incident.GetProperty("alertCount").GetInt32());
        Assert.Equal(
            ["host:production/web-01", "container:production/shop-api"],
            incident.GetProperty("blocks").EnumerateArray().Select(block => block.GetProperty("id").GetString()));
        Assert.Equal(JsonValueKind.Null, incident.GetProperty("acknowledgedAt").ValueKind);
        Assert.Single(open.GetProperty("items").EnumerateArray());
        Assert.Empty(resolved.GetProperty("items").EnumerateArray());
        Assert.NotEqual(disk.Id, container.Id);
    }

    [Fact]
    public async Task Get_Incident_HasItsAlertsFiguresTimelineAndRunbooks()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(
            postgres,
            ("Veyra:Alerting:Runbooks:disk-space", "https://wiki.example.com/runbooks/disk"));
        using var admin = await factory.CreateAdminClientAsync();
        var (disk, container) = await OpenHostIncidentAsync(factory);
        var id = await IncidentIdAsync(admin);

        var details = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/incidents/{id}");

        Assert.Equal(id, details.GetProperty("incident").GetProperty("id").GetGuid());
        Assert.Equal(
            [disk.Id, container.Id],
            details.GetProperty("alerts").EnumerateArray().Select(alert => alert.GetProperty("id").GetGuid()));
        Assert.All(details.GetProperty("alerts").EnumerateArray(), alert =>
            Assert.Equal("INC-1", alert.GetProperty("incidentKey").GetString()));
        var figures = details.GetProperty("figures");
        Assert.Equal(2, figures.GetProperty("affectedBlocks").GetInt32());
        Assert.True(figures.GetProperty("durationSeconds").GetDouble() >= 600);
        var leading = figures.GetProperty("leading");
        Assert.Equal((container.Id, "container-memory"), (leading.GetProperty("alertId").GetGuid(), leading.GetProperty("rule").GetString()));
        Assert.Equal((0.95, 0.9, 0.95), (
            leading.GetProperty("value").GetDouble(),
            leading.GetProperty("threshold").GetDouble(),
            leading.GetProperty("peak").GetDouble()));
        var timeline = details.GetProperty("timeline").EnumerateArray().ToList();
        Assert.Equal(["opened", "opened"], timeline.Select(moment => moment.GetProperty("kind").GetString()));
        Assert.Equal("host", timeline[0].GetProperty("block").GetProperty("kind").GetString());
        Assert.Equal("Caution", timeline[0].GetProperty("severity").GetString());
        Assert.Equal(0, timeline[0].GetProperty("deliveries").GetArrayLength());
        var runbook = Assert.Single(details.GetProperty("runbooks").EnumerateArray());
        Assert.Equal(
            ("disk-space", "https://wiki.example.com/runbooks/disk"),
            (runbook.GetProperty("rule").GetString(), runbook.GetProperty("url").GetString()));
    }

    [Fact]
    public async Task Acknowledge_AsAdmin_RecordsWhoOnTheIncidentAndItsTimelineUntilTakenBack()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await OpenHostIncidentAsync(factory);
        var id = await IncidentIdAsync(admin);

        using var acknowledged = await admin.PostAsync(new Uri($"/api/v1/incidents/{id}/acknowledge", UriKind.Relative), null);
        var item = await acknowledged.Content.ReadFromJsonAsync<JsonElement>();
        var details = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/incidents/{id}");
        using var takenBack = await admin.DeleteAsync(new Uri($"/api/v1/incidents/{id}/acknowledge", UriKind.Relative));
        var after = await takenBack.Content.ReadFromJsonAsync<JsonElement>();
        var timeline = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/incidents/{id}")).GetProperty("timeline");

        Assert.Equal(HttpStatusCode.OK, acknowledged.StatusCode);
        Assert.Equal(("acknowledged", "Root"), (item.GetProperty("state").GetString(), item.GetProperty("acknowledgedBy").GetString()));
        Assert.Equal(JsonValueKind.String, item.GetProperty("acknowledgedAt").ValueKind);
        Assert.Equal("acknowledged", details.GetProperty("incident").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, takenBack.StatusCode);
        Assert.Equal(("open", JsonValueKind.Null), (after.GetProperty("state").GetString(), after.GetProperty("acknowledgedBy").ValueKind));
        var moments = timeline.EnumerateArray().Where(moment => moment.GetProperty("alertId").ValueKind == JsonValueKind.Null).ToList();
        Assert.Equal(["acknowledged", "unacknowledged"], moments.Select(moment => moment.GetProperty("kind").GetString()));
        Assert.All(moments, moment => Assert.Equal("Root", moment.GetProperty("actor").GetString()));
    }

    [Fact]
    public async Task Acknowledge_AsViewer_IsForbiddenButReadingIsAllowed()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await OpenHostIncidentAsync(factory);
        var id = await IncidentIdAsync(admin);
        using var viewer = await CreateUserAsync(factory, admin, "Viewer");
        using var editor = await CreateUserAsync(factory, admin, "Editor");

        using var list = await viewer.GetAsync(new Uri("/api/v1/incidents", UriKind.Relative));
        using var forbidden = await viewer.PostAsync(new Uri($"/api/v1/incidents/{id}/acknowledge", UriKind.Relative), null);
        using var allowed = await editor.PostAsync(new Uri($"/api/v1/incidents/{id}/acknowledge", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Acknowledge_ResolvedIncident_IsAConflict()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var (disk, container) = await OpenHostIncidentAsync(factory);
        var store = factory.Services.GetRequiredService<AlertStore>();
        await store.ResolveAsync(disk.Id, 0.5, DateTimeOffset.UtcNow, CancellationToken.None);
        await store.ResolveAsync(container.Id, 0.5, DateTimeOffset.UtcNow, CancellationToken.None);
        var id = await IncidentIdAsync(admin);

        using var conflict = await admin.PostAsync(new Uri($"/api/v1/incidents/{id}/acknowledge", UriKind.Relative), null);
        var resolved = await admin.GetFromJsonAsync<JsonElement>("/api/v1/incidents?state=resolved");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("resolved", Assert.Single(resolved.GetProperty("items").EnumerateArray()).GetProperty("state").GetString());
    }

    [Fact]
    public async Task UnknownIncident_IsNotFoundEverywhere()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var id = Guid.CreateVersion7();

        using var details = await admin.GetAsync(new Uri($"/api/v1/incidents/{id}", UriKind.Relative));
        using var context = await admin.GetAsync(new Uri($"/api/v1/incidents/{id}/context", UriKind.Relative));
        using var acknowledge = await admin.PostAsync(new Uri($"/api/v1/incidents/{id}/acknowledge", UriKind.Relative), null);
        using var takeBack = await admin.DeleteAsync(new Uri($"/api/v1/incidents/{id}/acknowledge", UriKind.Relative));

        Assert.Equal(
            [HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound],
            new[] { details.StatusCode, context.StatusCode, acknowledge.StatusCode, takeBack.StatusCode });
    }

    [Theory]
    [InlineData("state=sideways")]
    [InlineData("days=0")]
    [InlineData("days=91")]
    [InlineData("limit=0")]
    [InlineData("limit=201")]
    [InlineData("cursor=not-a-cursor")]
    public async Task List_InvalidQuery_IsAnsweredWithAProblem(string query)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri($"/api/v1/incidents?{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri("/api/v1/incidents", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AlertDetails_AlertOfAnIncident_LinksIt()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var (disk, _) = await OpenHostIncidentAsync(factory);
        var id = await IncidentIdAsync(admin);

        var details = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{disk.Id}");
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/alerts");

        Assert.Equal((id, "INC-1"), (details.GetProperty("incidentId").GetGuid(), details.GetProperty("incidentKey").GetString()));
        Assert.All(list.GetProperty("items").EnumerateArray(), alert => Assert.Equal(id, alert.GetProperty("incidentId").GetGuid()));
    }

    [Fact]
    public async Task Context_FailingService_HasItsTracePathExceptionLogsSignalsAndComponents()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await factory.Services.GetRequiredService<ILogWriter>().WriteAsync(Records("payments-gateway", now), CancellationToken.None);
        await factory.Services.GetRequiredService<ISpanWriter>().WriteAsync(FailingTrace(now.AddSeconds(-30)), CancellationToken.None);
        await factory.Services.GetRequiredService<ErrorRateEvaluator>().EvaluateAsync(CancellationToken.None);
        var id = await IncidentIdAsync(admin);

        var context = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/incidents/{id}/context");

        Assert.True(context.GetProperty("from").GetDateTimeOffset() < now.AddMinutes(-29));
        var path = context.GetProperty("executionPath");
        Assert.Equal("trace", path.GetProperty("source").GetString());
        Assert.Equal(
            ["POST /pay", "charge card"],
            path.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("name").GetString()));
        var exception = context.GetProperty("exception");
        Assert.Equal(
            ("CardDeclinedException", "The card was declined", "trace"),
            (exception.GetProperty("type").GetString(), exception.GetProperty("message").GetString(), exception.GetProperty("source").GetString()));
        var logs = context.GetProperty("logs");
        Assert.Equal(10, logs.GetProperty("items").GetArrayLength());
        Assert.Equal(10, logs.GetProperty("total").GetInt64());
        Assert.Equal("service:payments-gateway AND env:production AND level>=warning", logs.GetProperty("query").GetString());
        Assert.Contains(
            context.GetProperty("timeline").EnumerateArray(),
            signal => signal.GetProperty("kind").GetString() == "first-error");
        var component = Assert.Single(context.GetProperty("components").EnumerateArray());
        Assert.Equal(
            ("service:production/payments-gateway", "warning", true, "affected"),
            (component.GetProperty("block").GetProperty("id").GetString(),
                component.GetProperty("health").GetString(),
                component.GetProperty("inIncident").GetBoolean(),
                component.GetProperty("relation").GetString()));
        Assert.Equal(JsonValueKind.Array, context.GetProperty("charts").ValueKind);
    }

    // A disk on web-01 fills, and ten minutes later a container on it runs out of memory: one incident.
    private static async Task<(AlertState Disk, AlertState Container)> OpenHostIncidentAsync(VeyraApiFactory factory)
    {
        var now = DateTimeOffset.UtcNow;
        var disk = new AlertState(
            Guid.CreateVersion7(),
            AlertRules.DiskSpace,
            "production",
            AlertSubject.Host("web-01", "/"),
            AlertSeverity.Caution,
            0.92,
            0.92,
            0.9,
            TimeSpan.FromMinutes(5),
            now.AddMinutes(-20),
            now,
            null);
        var container = disk with
        {
            Id = Guid.CreateVersion7(),
            Rule = AlertRules.ContainerMemory,
            Subject = AlertSubject.Container("shop-api", "web-01"),
            Severity = AlertSeverity.Warning,
            Value = 0.95,
            Peak = 0.95,
            OpenedAt = now.AddMinutes(-10),
        };
        var store = factory.Services.GetRequiredService<AlertStore>();
        await store.InsertAsync(disk, CancellationToken.None);
        await store.InsertAsync(container, CancellationToken.None);

        return (disk, container);
    }

    private static async Task<Guid> IncidentIdAsync(HttpClient client)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/incidents");

        return list.GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    private static async Task<HttpClient> CreateUserAsync(VeyraApiFactory factory, HttpClient admin, string role)
    {
        var email = $"{role.ToLowerInvariant()}@example.com";
        using var created = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email, displayName = role, role, password = _password });
        created.EnsureSuccessStatusCode();

        return await factory.CreateSignedInClientAsync(email, _password);
    }

    // 40 records in the last minute, a quarter of them errors: a warning for the error-rate rule.
    private static LogRecord[] Records(string service, DateTimeOffset now) =>
        Enumerable.Range(0, 40)
            .Select(index =>
            {
                var severity = index % 4 == 0 ? LogSeverity.Error : LogSeverity.Information;
                var timestamp = now.AddSeconds(-index);

                return new LogRecord(
                    timestamp,
                    timestamp,
                    severity,
                    severity.ToString(),
                    severity == LogSeverity.Error ? "Payment failed" : "ok",
                    service,
                    "production",
                    null,
                    null,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["service.name"] = service });
            })
            .ToArray();

    // POST /pay failed because charging the card threw.
    private static Span[] FailingTrace(DateTimeOffset start)
    {
        const string trace = "0af7651916cd43dd8448eb211c80319c";

        Span Make(string id, string? parent, string name, int offset, int duration, bool exception) => new(
            trace,
            id,
            parent,
            name,
            SpanKind.Server,
            start.AddMilliseconds(offset),
            TimeSpan.FromMilliseconds(duration),
            SpanStatus.Error,
            exception ? "card declined" : "payment failed",
            "payments-gateway",
            "production",
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            exception
                ?
                [
                    new SpanEvent(start.AddMilliseconds(offset + 5), "exception", new Dictionary<string, string>
                    {
                        ["exception.type"] = "CardDeclinedException",
                        ["exception.message"] = "The card was declined",
                        ["exception.stacktrace"] = "at Charge()",
                    }),
                ]
                : [],
            [],
            SpanDropped.None);

        return [Make("b7ad6b7169203331", null, "POST /pay", 0, 200, false), Make("00f067aa0ba902b7", "b7ad6b7169203331", "charge card", 20, 150, true)];
    }
}
