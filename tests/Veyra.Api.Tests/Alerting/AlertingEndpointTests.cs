using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Alerting.ErrorRates;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Api.Tests.Alerting;

[Collection(PostgresGroup.Name)]
public class AlertingEndpointTests(PostgresFixture postgres)
{
    private const string _password = "a long enough password";

    private const string _webhookUrl = "https://hooks.slack.com/services/T0A1/B0C2/secret-token";

    [Fact]
    public async Task GetAlert_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri($"/api/v1/alerts/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAlert_Unknown_ReturnsNotFound()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri($"/api/v1/alerts/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAlert_AfterTheRuleFires_ShowsTimelineDeliveryChartAndTopErrorsToViewers()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var created = await admin.PostAsJsonAsync("/api/v1/notification-channels", Webhook("#ops-alerts"));
        created.EnsureSuccessStatusCode();
        await factory.Services.GetRequiredService<ILogWriter>().WriteAsync(
            Records("payments-gateway", total: 40, errors: 10),
            CancellationToken.None);
        await factory.Services.GetRequiredService<ErrorRateEvaluator>().EvaluateAsync(CancellationToken.None);
        var home = await admin.GetFromJsonAsync<Home>("/api/v1/home");
        await CreateUserAsync(admin, "viewer@example.com", "Viewer");
        using var viewer = await factory.CreateSignedInClientAsync("viewer@example.com", _password);

        var alert = await viewer.GetFromJsonAsync<AlertDetails>($"/api/v1/alerts/{home!.Alerts.Single().Id}");

        Assert.Equal(("payments-gateway", "Warning"), (alert!.Service, alert.Severity));
        var opened = Assert.Single(alert.Events);
        Assert.Equal("Opened", opened.Kind);
        var delivery = Assert.Single(opened.Deliveries);
        Assert.Equal(
            ("#ops-alerts", "Webhook", "Pending"),
            (delivery.ChannelName, delivery.ChannelKind, delivery.State));
        Assert.Equal(60, alert.Series.BucketSeconds);
        Assert.Contains(alert.Series.Points, point => point.Ratio > 0);
        Assert.Equal(5, alert.TopErrors.Count);
        Assert.Equal(new TopErrorGroup("failed 0", 1), alert.TopErrors[0]);
    }

    [Fact]
    public async Task Channels_Viewer_IsForbidden()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await CreateUserAsync(admin, "viewer@example.com", "Viewer");
        using var viewer = await factory.CreateSignedInClientAsync("viewer@example.com", _password);

        using var response = await viewer.GetAsync(new Uri("/api/v1/notification-channels", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Channels_CreateWebhook_NeverShowsTheUrlAgain()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var created = await admin.PostAsJsonAsync("/api/v1/notification-channels", Webhook("#ops-alerts"));
        var createdBody = await created.Content.ReadAsStringAsync();
        var list = await admin.GetStringAsync(new Uri("/api/v1/notification-channels", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("https://hooks.slack.com/services/T0…", createdBody, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", createdBody, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Channels_List_ShowsTheEmailServerAndEachChannelWithFourteenDays()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        (await admin.PostAsJsonAsync("/api/v1/notification-channels", Email("On-call"))).EnsureSuccessStatusCode();

        var list = await admin.GetFromJsonAsync<ChannelList>("/api/v1/notification-channels");

        Assert.False(list!.EmailServer.Configured);
        var channel = Assert.Single(list.Channels);
        Assert.Equal(("On-call", "Email"), (channel.Name, channel.Kind));
        Assert.Equal(["oncall@example.com"], channel.EmailRecipients);
        Assert.Equal(14, channel.Activity.Days.Count);
    }

    [Fact]
    public async Task Channels_InvalidOrDuplicate_AreAnsweredWithProblems()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        (await admin.PostAsJsonAsync("/api/v1/notification-channels", Webhook("ops"))).EnsureSuccessStatusCode();

        using var plainHttp = await admin.PostAsJsonAsync(
            "/api/v1/notification-channels",
            Webhook("other") with { WebhookUrl = "http://hooks.example.com/x" });
        using var duplicate = await admin.PostAsJsonAsync("/api/v1/notification-channels", Webhook("OPS"));

        Assert.Equal(HttpStatusCode.BadRequest, plainHttp.StatusCode);
        var problem = await plainHttp.Content.ReadFromJsonAsync<Problem>();
        Assert.Contains("https", problem!.Title, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Channels_UpdateAndDelete_ChangeAndRemoveTheChannel()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var created = await admin.PostAsJsonAsync("/api/v1/notification-channels", Webhook("ops"));
        var channel = (await created.Content.ReadFromJsonAsync<Channel>())!;

        using var updated = await admin.PutAsJsonAsync(
            $"/api/v1/notification-channels/{channel.Id}",
            Webhook("ops-alerts") with { WebhookUrl = null, Enabled = false });
        var address = new Uri($"/api/v1/notification-channels/{channel.Id}", UriKind.Relative);
        using var deleted = await admin.DeleteAsync(address);
        using var again = await admin.DeleteAsync(address);

        var changed = (await updated.Content.ReadFromJsonAsync<Channel>())!;
        Assert.Equal(("ops-alerts", false), (changed.Name, changed.Enabled));
        Assert.Equal(channel.WebhookUrlHint, changed.WebhookUrlHint);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Channels_TestEmailWithoutServer_SaysWhatToSet()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync("/api/v1/notification-channels/test", Email("On-call"));
        var result = await response.Content.ReadFromJsonAsync<TestResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result!.Succeeded);
        Assert.Contains("VEYRA_SMTP_HOST", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Channels_WithoutAntiforgeryHeader_AreRefused()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        admin.DefaultRequestHeaders.Remove("X-Veyra-Antiforgery");

        using var response = await admin.PostAsJsonAsync("/api/v1/notification-channels", Webhook("ops"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static ChannelInput Webhook(string name) =>
        new(name, "Webhook", true, true, true, true, _webhookUrl, null);

    private static ChannelInput Email(string name) =>
        new(name, "Email", true, true, false, true, null, ["oncall@example.com"]);

    private static async Task CreateUserAsync(HttpClient admin, string email, string role)
    {
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email, displayName = email, role, password = _password });
        response.EnsureSuccessStatusCode();
    }

    // Over the last two minutes, inside the rule's five-minute window. Each error has its own message, so the top
    // errors list them one by one.
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
                    index < errors ? $"failed {index}" : "ok",
                    service,
                    "production",
                    null,
                    null,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string> { ["service.name"] = service });
            })
            .ToArray();
    }

    private sealed record ChannelInput(
        string Name,
        string Kind,
        bool Enabled,
        bool SendWarning,
        bool SendCaution,
        bool SendResolved,
        string? WebhookUrl,
        string[]? EmailRecipients);

    private sealed record Home(List<HomeAlert> Alerts);

    private sealed record HomeAlert(Guid Id);

    private sealed record AlertDetails(
        string Service,
        string Severity,
        List<Event> Events,
        Series Series,
        List<TopErrorGroup> TopErrors);

    private sealed record Event(string Kind, List<Delivery> Deliveries);

    private sealed record Delivery(string ChannelName, string ChannelKind, string State);

    private sealed record Series(int BucketSeconds, List<Point> Points);

    private sealed record Point(double? Ratio);

    private sealed record TopErrorGroup(string Message, long Count);

    private sealed record ChannelList(EmailServer EmailServer, List<Channel> Channels);

    private sealed record EmailServer(bool Configured);

    private sealed record Channel(
        Guid Id,
        string Name,
        string Kind,
        bool Enabled,
        string? WebhookUrlHint,
        List<string>? EmailRecipients,
        Activity Activity);

    private sealed record Activity(List<object> Days);

    private sealed record Problem(string Title);

    private sealed record TestResult(bool Succeeded, string? Error);
}
