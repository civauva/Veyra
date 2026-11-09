using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Api.Tests.Ingestion;

[Collection(PostgresGroup.Name)]
public class OtlpHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PostLogs_ProtobufWithValidKey_StoresTheRecords()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        var key = await CreateKeyAsync(factory);
        using var client = SenderClient(factory, key);

        using var response = await client.PostAsync(
            new Uri("/v1/logs", UriKind.Relative),
            Protobuf(Request("payment declined")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);
        var stored = await WaitForAsync(factory, "payment declined");
        Assert.Equal("payments", stored.ServiceName);
        Assert.Equal(LogSeverity.Error, stored.Severity);
    }

    [Fact]
    public async Task PostLogs_JsonWithHexIds_StoresTheTraceId()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        var key = await CreateKeyAsync(factory);
        using var client = SenderClient(factory, key);
        const string json = """
            {"resourceLogs":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"orders"}}]},
             "scopeLogs":[{"logRecords":[{"timeUnixNano":"1794225600000000000","severityNumber":13,
             "body":{"stringValue":"order delayed"},"traceId":"5B8EFFF798038103D269B633813FC60C",
             "spanId":"EEE19B7EC3C1B174"}]}]}]}
            """;

        using var response = await client.PostAsync(
            new Uri("/v1/logs", UriKind.Relative),
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await WaitForAsync(factory, "order delayed");
        Assert.Equal("5b8efff798038103d269b633813fc60c", stored.TraceId);
        Assert.Equal("eee19b7ec3c1b174", stored.SpanId);
        Assert.Equal(LogSeverity.Warning, stored.Severity);
    }

    [Fact]
    public async Task PostLogs_Gzipped_IsAccepted()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        var key = await CreateKeyAsync(factory);
        using var client = SenderClient(factory, key);
        using var compressed = new MemoryStream();

        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            Request("compressed").WriteTo(gzip);
        }

        var content = new ByteArrayContent(compressed.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        content.Headers.ContentEncoding.Add("gzip");

        using var response = await client.PostAsync(new Uri("/v1/logs", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitForAsync(factory, "compressed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("vyk_ThisKeyHasTheRightShapeButDoesNotExistAnywhere1")]
    public async Task PostLogs_MissingOrUnknownKey_ReturnsUnauthorized(string? key)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var client = SenderClient(factory, key);

        using var response = await client.PostAsync(new Uri("/v1/logs", UriKind.Relative), Protobuf(Request("x")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLogs_ManyWrongKeys_AreThrottledWithTooManyRequests()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var client = SenderClient(factory, "vyk_ThisKeyHasTheRightShapeButDoesNotExistAnywhere1");
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 21; attempt++)
        {
            using var response = await client.PostAsync(new Uri("/v1/logs", UriKind.Relative), Protobuf(Request("x")));
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(20), status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[20]);
    }

    [Fact]
    public async Task PostLogs_KeyForOtherEnvironment_ReportsPartialSuccess()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        var key = await CreateKeyAsync(factory, environments: ["staging"]);
        using var client = SenderClient(factory, key);

        using var response = await client.PostAsync(
            new Uri("/v1/logs", UriKind.Relative),
            Protobuf(Request("wrong environment", environment: "production")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = ExportLogsServiceResponse.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, body.PartialSuccess.RejectedLogRecords);
        Assert.Contains("production", body.PartialSuccess.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostLogs_NotProtobuf_ReturnsBadRequest()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        var key = await CreateKeyAsync(factory);
        using var client = SenderClient(factory, key);
        var content = new ByteArrayContent([0xFF, 0xFF, 0xFF, 0x01]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        using var response = await client.PostAsync(new Uri("/v1/logs", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostLogs_UnsupportedContentType_ReturnsUnsupportedMediaType()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        var key = await CreateKeyAsync(factory);
        using var client = SenderClient(factory, key);

        using var response = await client.PostAsync(
            new Uri("/v1/logs", UriKind.Relative),
            new StringContent("hello", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    private static async Task<string> CreateKeyAsync(VeyraApiFactory factory, string[]? environments = null)
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/ingestion-keys",
            new { name = "test sender", environments });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedKey>())!.Secret;
    }

    private static HttpClient SenderClient(VeyraApiFactory factory, string? key)
    {
        var client = factory.CreateHttpsClient();

        if (key is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        return client;
    }

    private static ExportLogsServiceRequest Request(string body, string environment = "production")
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
            Value = new AnyValue { StringValue = environment },
        });

        var scope = new ScopeLogs();
        scope.LogRecords.Add(new OpenTelemetry.Proto.Logs.V1.LogRecord
        {
            TimeUnixNano = (ulong)(DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100,
            SeverityNumber = SeverityNumber.Error,
            Body = new AnyValue { StringValue = body },
        });

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

    // Ingestion is asynchronous: the answer comes when records are queued, the database write follows shortly after.
    private static async Task<Core.Telemetry.LogRecord> WaitForAsync(VeyraApiFactory factory, string body)
    {
        var search = factory.Services.GetRequiredService<ILogSearch>();
        var window = new LogQuery(
            DateTimeOffset.UtcNow.AddYears(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            null,
            null,
            null,
            null,
            100,
            null);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var page = await search.SearchAsync(window, CancellationToken.None);
            var match = page.Items.FirstOrDefault(item => item.Record.Body == body);

            if (match is not null)
            {
                return match.Record;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException($"No stored record with body \"{body}\".");
    }

    private sealed record CreatedKey(string Secret);
}
