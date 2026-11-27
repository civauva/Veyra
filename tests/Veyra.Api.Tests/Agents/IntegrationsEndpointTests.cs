using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using OpAmp.Proto.V1;
using Veyra.Agents.Agents;
using Veyra.Testing;

namespace Veyra.Api.Tests.Agents;

/// <summary>The Integrations endpoints of ADR 0028, through the whole application.</summary>
[Collection(PostgresGroup.Name)]
public sealed class IntegrationsEndpointTests(PostgresFixture postgres) : IDisposable
{
    private const string _password = "a long enough password";

    private readonly string _directory = Directory.CreateTempSubdirectory("veyra-integrations-api-").FullName;

    [Fact]
    public async Task SqlServers_InstanceAuthenticationAndModules_ComeBackAndShowAsAnIntegration()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var created = await admin.PostAsJsonAsync("/api/v1/sql-servers", new
        {
            name = "sql-prod-01",
            address = @"sql01.corp.local\REPORTS",
            environment = "production",
            login = "veyra_monitor",
            password = "Correct-Horse-7",
            encryption = "TrustServerCertificate",
            authentication = "sql",
            modules = new[] { new { id = "activity", enabled = false, intervalSeconds = 10 } },
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var server = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("sql01.corp.local", server.GetProperty("address").GetString());
        Assert.Equal("REPORTS", server.GetProperty("instance").GetString());
        Assert.Equal(1433, server.GetProperty("port").GetInt32());
        Assert.Equal("sql", server.GetProperty("authentication").GetString());
        Assert.Equal("0.2.0", server.GetProperty("version").GetString());
        Assert.False(Module(server.GetProperty("modules"), "activity").GetProperty("enabled").GetBoolean());

        var list = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/integrations")).GetProperty("items");
        var item = Assert.Single(list.EnumerateArray());
        Assert.Equal("sqlserver", item.GetProperty("kind").GetString());
        Assert.Equal("Built in", item.GetProperty("agent").GetProperty("name").GetString());
        Assert.Equal("0.2.0", item.GetProperty("latestVersion").GetString());
        Assert.Equal("AgentNotConnected", item.GetProperty("state").GetString());
        Assert.Equal(15, Module(item.GetProperty("modules"), "core").GetProperty("intervalSeconds").GetInt32());
    }

    [Fact]
    public async Task Modules_SqlServer_ListsEveryModuleWithItsChoices()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        var modules = await admin.GetFromJsonAsync<JsonElement>("/api/v1/integrations/modules/sqlserver");

        Assert.Equal(9, modules.GetArrayLength());
        var core = modules[0];
        Assert.Equal(("core", true, 15), (
            core.GetProperty("id").GetString(),
            core.GetProperty("required").GetBoolean(),
            core.GetProperty("defaultIntervalSeconds").GetInt32()));
        Assert.Equal([10, 15, 30, 60], core.GetProperty("choices").EnumerateArray().Select(one => one.GetInt32()));
    }

    [Fact]
    public async Task Detail_RollBackThenUpdate_IsInThePackageAndTheHistory()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var id = await AddServerAsync(admin);

        using var back = await admin.PostAsJsonAsync(
            $"/api/v1/integrations/sqlserver/{id}/version",
            new { version = "0.1.0" });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        var moved = JsonDocument.Parse(await back.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("0.1.0", moved.GetProperty("version").GetString());

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/integrations/sqlserver/{id}?range=1h");

        var package = detail.GetProperty("package");
        Assert.Equal("veyra-sqlserver-integration", package.GetProperty("name").GetString());
        Assert.Equal("0.1.0", package.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, package.GetProperty("previousVersion").ValueKind);
        Assert.Equal("Root", package.GetProperty("setBy").GetString());
        var change = detail.GetProperty("history")[0];
        Assert.Equal(("version", "0.2.0", "0.1.0"), (
            change.GetProperty("kind").GetString(),
            change.GetProperty("from").GetString(),
            change.GetProperty("to").GetString()));
        Assert.Equal("Root", change.GetProperty("by").GetString());
        Assert.Equal(9, detail.GetProperty("modules").GetArrayLength());
        Assert.Equal(60, detail.GetProperty("modules")[0].GetProperty("line").GetArrayLength());
        Assert.Contains(
            detail.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("name").GetString() == "permissions");
        Assert.Equal(3, detail.GetProperty("nextSteps").GetArrayLength());
        Assert.Equal("unknown", detail.GetProperty("collector").GetProperty("configState").GetString());
    }

    [Fact]
    public async Task Version_NotShipped_IsABadRequest()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var id = await AddServerAsync(admin);

        using var response = await admin.PostAsJsonAsync(
            $"/api/v1/integrations/sqlserver/{id}/version",
            new { version = "0.0.1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Detail_UnknownServerOrBadWindow_IsNotFoundOrABadRequest()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var id = await AddServerAsync(admin);

        using var unknown = await admin.GetAsync(
            new Uri($"/api/v1/integrations/sqlserver/{Guid.NewGuid()}", UriKind.Relative));
        using var backwards = await admin.GetAsync(new Uri(
            $"/api/v1/integrations/sqlserver/{id}?from=2026-11-26T10:00:00Z&to=2026-11-26T09:00:00Z",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
    }

    [Fact]
    public async Task Probe_AgentNeverConnected_FailsAtOnceWithAgentOffline()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var started = await admin.PostAsJsonAsync("/api/v1/integrations/probes", Probe());

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var id = JsonDocument.Parse(await started.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();
        var probe = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/integrations/probes/{id}");
        Assert.Equal("failed", probe.GetProperty("state").GetString());
        Assert.Equal("agent-offline", probe.GetProperty("error").GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, probe.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task Probe_OnlineAgent_IsSentInItsConfigurationAndSucceedsWithItsAnswer()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var key = BuiltInAgentSetup.ReadKey(
            await File.ReadAllTextAsync(Path.Combine(_directory, BuiltInAgentSetup.FileName)))!;
        using var agent = factory.CreateHttpsClient();
        await PollAsync(agent, key);

        using var started = await admin.PostAsJsonAsync("/api/v1/integrations/probes", Probe());
        var id = Guid.Parse(
            JsonDocument.Parse(await started.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!);
        Assert.Equal(
            "pending",
            (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/integrations/probes/{id}"))
                .GetProperty("state").GetString());

        var config = await PollAsync(agent, key);
        Assert.Contains($"sqlquery/probe/{id:N}", config, StringComparison.Ordinal);
        Assert.Contains("veyra_monitor:Probe-Horse-2@sql-new:1433", config, StringComparison.Ordinal);

        using var answer = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/logs", UriKind.Relative))
        {
            Content = new StringContent(Answer(id), Encoding.UTF8, "application/json"),
        };
        answer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var sent = await agent.SendAsync(answer);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        var probe = await WaitForProbeAsync(admin, id);
        Assert.Equal("succeeded", probe.GetProperty("state").GetString());
        var result = probe.GetProperty("result");
        Assert.Equal("16.0.4135.4", result.GetProperty("version").GetString());
        Assert.Equal(7, result.GetProperty("databases").GetInt32());
        Assert.Equal(
            ["master", "shop"],
            result.GetProperty("databaseNames").EnumerateArray().Select(one => one.GetString()));
        Assert.Equal(
            ["VIEW SERVER STATE"],
            result.GetProperty("missingPermissions").EnumerateArray().Select(one => one.GetString()));
        Assert.DoesNotContain($"sqlquery/probe/{id:N}", await PollAsync(agent, key), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_UnknownServer_IsNotFound()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync(
            "/api/v1/integrations/probes",
            new { serverId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Editor")]
    public async Task Integrations_NotAnAdmin_IsForbidden(string role)
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        using var user = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "user@example.com", displayName = "User", role, password = _password });
        user.EnsureSuccessStatusCode();
        using var client = await factory.CreateSignedInClientAsync("user@example.com", _password);

        using var list = await client.GetAsync(new Uri("/api/v1/integrations", UriKind.Relative));
        using var probe = await client.PostAsJsonAsync("/api/v1/integrations/probes", Probe());

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, probe.StatusCode);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private Task<VeyraApiFactory> CreateAsync() =>
        VeyraApiFactory.CreateAsync(postgres, ("Veyra:Agents:BuiltInDirectory", _directory));

    private static object Probe() =>
        new
        {
            address = "sql-new",
            login = "veyra_monitor",
            password = "Probe-Horse-2",
            encryption = "Off",
        };

    private static async Task<string> AddServerAsync(HttpClient admin)
    {
        using var created = await admin.PostAsJsonAsync("/api/v1/sql-servers", new
        {
            name = "sql-prod-01",
            address = "10.0.0.5",
            port = 1433,
            environment = "production",
            login = "veyra_monitor",
            password = "Correct-Horse-7",
            encryption = "Required",
        });
        created.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
    }

    private static JsonElement Module(JsonElement modules, string id) =>
        modules.EnumerateArray().Single(module => module.GetProperty("id").GetString() == id);

    // The probe's answer as the agent's sqlquery receiver sends it: one log record, every column a text attribute.
    private static string Answer(Guid id)
    {
        static object Attribute(string key, string value) => new { key, value = new { stringValue = value } };

        return JsonSerializer.Serialize(new
        {
            resourceLogs = new[]
            {
                new
                {
                    resource = new { attributes = new[] { Attribute("service.name", "otelcol-contrib") } },
                    scopeLogs = new[]
                    {
                        new
                        {
                            logRecords = new[]
                            {
                                new
                                {
                                    timeUnixNano = "1794225600000000000",
                                    body = new { stringValue = "probe" },
                                    attributes = new[]
                                    {
                                        Attribute("veyra_record", "sqlserver.probe"),
                                        Attribute("probe", id.ToString("N")),
                                        Attribute("version", "16.0.4135.4"),
                                        Attribute("update_level", "CU14"),
                                        Attribute("edition", "Developer Edition (64-bit)"),
                                        Attribute("platform", "sqlserver"),
                                        Attribute("databases", "7"),
                                        Attribute("database_names", """[{"name":"master"},{"name":"shop"}]"""),
                                        Attribute("missing", """[{"permission":"VIEW SERVER STATE"}]"""),
                                    },
                                },
                            },
                        },
                    },
                },
            },
        });
    }

    private static async Task<JsonElement> WaitForProbeAsync(HttpClient admin, Guid id)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (true)
        {
            var probe = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/integrations/probes/{id}");

            if (probe.GetProperty("state").GetString() != "pending" || DateTime.UtcNow > deadline)
            {
                return probe;
            }

            await Task.Delay(100);
        }
    }

    // One OpAMP poll of the agent, which also makes it online; returns the configuration it is sent.
    private static async Task<string> PollAsync(HttpClient agent, string key)
    {
        var message = new AgentToServer
        {
            InstanceUid = ByteString.CopyFrom(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray()),
            AgentDescription = new AgentDescription
            {
                IdentifyingAttributes =
                {
                    new KeyValue { Key = "service.version", Value = new AnyValue { StringValue = "0.140.1" } },
                },
            },
            Health = new ComponentHealth { Healthy = true },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/opamp", UriKind.Relative))
        {
            Content = new ByteArrayContent(message.ToByteArray()),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await agent.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var reply = ServerToAgent.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());

        return reply.RemoteConfig.Config.ConfigMap[""].Body.ToStringUtf8();
    }
}
