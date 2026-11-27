using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using Veyra.Core.Telemetry;
using Veyra.Query.Streams;
using Veyra.Testing;

namespace Veyra.Api.Tests.Query;

[Collection(PostgresGroup.Name)]
public class LiveStreamEndpointTests(PostgresFixture postgres)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("/api/v1/live")]
    [InlineData("/api/v1/logs/stream")]
    public async Task GetStream_Anonymous_ReturnsUnauthorized(string url)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLive_Connected_SendsTheRateAtOnceAndATickWithinFiveSeconds()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        factory.Services.GetRequiredService<ILiveTelemetry>().SpansWritten([new LiveCount("production", 40)]);

        using var response = await OpenAsync(admin, "/api/v1/live?environment=production");
        using var events = new EventReader(await response.Content.ReadAsStreamAsync());

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no", response.Headers.GetValues("X-Accel-Buffering").Single());
        var rate = await events.NextAsync("rate");
        Assert.True(rate.Data.TryGetProperty("logs", out _));
        Assert.True(rate.Data.TryGetProperty("points", out _));
        Assert.InRange(rate.Data.GetProperty("spans").GetDouble(), 0, 4);
        var tick = await events.NextAsync("tick");
        Assert.True(tick.Data.GetProperty("at").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task GetLogsStream_RecordIngestedOverOtlp_IsSentWhenItMatches()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var sender = await SenderAsync(factory, admin);

        using var response = await OpenAsync(admin, "/api/v1/logs/stream?severity=Error&text=declined");
        using var events = new EventReader(await response.Content.ReadAsStreamAsync());
        await events.OpenedAsync();

        using var accepted = await sender.PostAsync(
            new Uri("/v1/logs", UriKind.Relative),
            Protobuf(Request(("all fine", SeverityNumber.Info), ("payment declined", SeverityNumber.Error))));
        accepted.EnsureSuccessStatusCode();

        var record = await events.NextAsync("record");
        Assert.Equal("payment declined", record.Data.GetProperty("body").GetString());
        Assert.Equal("Error", record.Data.GetProperty("severity").GetString());
        Assert.Equal("payments", record.Data.GetProperty("serviceName").GetString());
        Assert.Equal("production", record.Data.GetProperty("environment").GetString());
        Assert.True(record.Data.GetProperty("id").GetInt64() > 0);
        Assert.True(record.Data.TryGetProperty("resourceAttributes", out _));
    }

    [Fact]
    public async Task GetLogsStream_BrowserFallsBehind_SaysHowManyRecordsItSkipped()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var live = factory.Services.GetRequiredService<ILiveTelemetry>();

        using var response = await OpenAsync(admin, "/api/v1/logs/stream");
        using var events = new EventReader(await response.Content.ReadAsStreamAsync());
        await events.OpenedAsync();

        // Far more than one listener's queue at once, faster than any stream can send them.
        live.LogsWritten(Enumerable.Range(0, 5_000).Select(index => Log($"burst {index}")).ToList());

        var skipped = await events.NextAsync("skipped", maxEvents: 6_000);
        Assert.InRange(skipped.Data.GetProperty("count").GetInt64(), 1, 4_000);
    }

    [Fact]
    public async Task GetLive_MoreListenersThanTheSetting_RefusesTheNextWithServiceUnavailable()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres, ("Veyra:Query:LiveListeners", "1"));
        using var admin = await factory.CreateAdminClientAsync();

        using var first = await OpenAsync(admin, "/api/v1/live");
        using var second = await admin.GetAsync(
            new Uri("/api/v1/logs/stream", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetLogsStream_BrowserLeaves_FreesItsPlace()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var hub = factory.Services.GetRequiredService<LiveTelemetryHub>();

        var response = await OpenAsync(admin, "/api/v1/logs/stream");
        var events = new EventReader(await response.Content.ReadAsStreamAsync());
        await events.OpenedAsync();
        Assert.Equal(1, hub.Listeners);

        events.Dispose();
        response.Dispose();

        var deadline = DateTime.UtcNow + _wait;

        while (hub.Listeners > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(0, hub.Listeners);
    }

    [Fact]
    public async Task GetLogsStream_BrokenAttributeFilter_ReturnsBadRequest()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri("/api/v1/logs/stream?attribute=broken", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> OpenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(new Uri(url, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        return response;
    }

    private static async Task<HttpClient> SenderAsync(VeyraApiFactory factory, HttpClient admin)
    {
        using var created = await admin.PostAsJsonAsync("/api/v1/ingestion-keys", new { name = "live sender" });
        created.EnsureSuccessStatusCode();
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString();

        var sender = factory.CreateHttpsClient();
        sender.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        return sender;
    }

    private static ExportLogsServiceRequest Request(params (string Body, SeverityNumber Severity)[] records)
    {
        var resource = new Resource();
        resource.Attributes.Add(new KeyValue
        {
            Key = "service.name",
            Value = new AnyValue { StringValue = "payments" },
        });
        resource.Attributes.Add(new KeyValue
        {
            Key = "deployment.environment.name",
            Value = new AnyValue { StringValue = "production" },
        });

        var scope = new ScopeLogs();

        foreach (var (body, severity) in records)
        {
            scope.LogRecords.Add(new OpenTelemetry.Proto.Logs.V1.LogRecord
            {
                TimeUnixNano = (ulong)(DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100,
                SeverityNumber = severity,
                Body = new AnyValue { StringValue = body },
            });
        }

        var resourceLogs = new ResourceLogs { Resource = resource };
        resourceLogs.ScopeLogs.Add(scope);

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLogs);

        return request;
    }

    private static ByteArrayContent Protobuf(IMessage message)
    {
        var content = new ByteArrayContent(message.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        return content;
    }

    private static Core.Telemetry.LogRecord Log(string body) => new(
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        LogSeverity.Information,
        "Information",
        body,
        "payments",
        "production",
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string>());

    /// <summary>Reads server-sent events one at a time, as a browser's EventSource does.</summary>
    private sealed class EventReader(Stream stream) : IDisposable
    {
        private readonly StreamReader _reader = new(stream);

        /// <summary>Waits for the comment the server sends as the stream opens.</summary>
        public async Task OpenedAsync()
        {
            using var timeout = new CancellationTokenSource(_wait);
            var line = await _reader.ReadLineAsync(timeout.Token);

            Assert.Equal(": open", line);
        }

        /// <summary>Returns the next event with the name, skipping comments and other events.</summary>
        public async Task<(string Name, JsonElement Data)> NextAsync(string name, int maxEvents = 100)
        {
            using var timeout = new CancellationTokenSource(_wait);
            string? current = null;
            string? data = null;
            var seen = 0;

            while (seen < maxEvents)
            {
                var line = await _reader.ReadLineAsync(timeout.Token)
                    ?? throw new Xunit.Sdk.XunitException("The stream ended.");

                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    current = line["event: ".Length..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line["data: ".Length..];
                }
                else if (line.Length == 0 && current is not null)
                {
                    seen++;

                    if (current == name)
                    {
                        return (current, JsonDocument.Parse(data!).RootElement.Clone());
                    }

                    current = null;
                    data = null;
                }
            }

            throw new Xunit.Sdk.XunitException($"No {name} event among {maxEvents}.");
        }

        public void Dispose() => _reader.Dispose();
    }
}
