using System.Net.Http.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Api.Tests.Ingestion;

[Collection(PostgresGroup.Name)]
public class OtlpGrpcTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Export_ValidKey_StoresTheRecords()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        var key = await CreateKeyAsync(factory);
        using var channel = Channel(factory);

        var response = await new LogsService.LogsServiceClient(channel).ExportAsync(
            Request("grpc record", "production"),
            Bearer(key));

        Assert.Null(response.PartialSuccess);
        await WaitForAsync(factory, "grpc record");
    }

    [Fact]
    public async Task Export_NoKey_ReturnsUnauthenticated()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var channel = Channel(factory);

        var error = await Assert.ThrowsAsync<RpcException>(async () =>
            await new LogsService.LogsServiceClient(channel).ExportAsync(Request("x", "production")));

        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
    }

    [Fact]
    public async Task Export_KeyForOtherEnvironment_ReportsPartialSuccess()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        var key = await CreateKeyAsync(factory, ["staging"]);
        using var channel = Channel(factory);

        var response = await new LogsService.LogsServiceClient(channel).ExportAsync(
            Request("wrong environment", "production"),
            Bearer(key));

        Assert.Equal(1, response.PartialSuccess.RejectedLogRecords);
    }

    private static GrpcChannel Channel(VeyraApiFactory factory) =>
        GrpcChannel.ForAddress(
            "https://localhost",
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });

    private static Metadata Bearer(string key) => new() { { "authorization", $"Bearer {key}" } };

    private static async Task<string> CreateKeyAsync(VeyraApiFactory factory, string[]? environments = null)
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/ingestion-keys",
            new { name = "grpc sender", environments });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedKey>())!.Secret;
    }

    private static ExportLogsServiceRequest Request(string body, string environment)
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
            SeverityNumber = SeverityNumber.Info,
            Body = new AnyValue { StringValue = body },
        });

        var resourceLogs = new ResourceLogs { Resource = resource };
        resourceLogs.ScopeLogs.Add(scope);

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLogs);

        return request;
    }

    private static async Task WaitForAsync(VeyraApiFactory factory, string body)
    {
        var search = factory.Services.GetRequiredService<ILogSearch>();
        var now = DateTimeOffset.UtcNow;
        var window = new LogQuery(now.AddDays(-1), now.AddDays(1), null, null, null, null, 100, null);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var page = await search.SearchAsync(window, CancellationToken.None);

            if (page.Items.Any(item => item.Record.Body == body))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException($"No stored record with body \"{body}\".");
    }

    private sealed record CreatedKey(string Secret);
}
