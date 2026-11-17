using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using OpAmp.Proto.V1;
using Veyra.Agents.Agents;
using Veyra.Testing;

namespace Veyra.Api.Tests.Agents;

[Collection(PostgresGroup.Name)]
public sealed class AgentsEndpointTests(PostgresFixture postgres) : IDisposable
{
    private const string _password = "a long enough password";

    private readonly string _directory = Directory.CreateTempSubdirectory("veyra-agent-api-").FullName;

    [Fact]
    public async Task Agents_FirstStart_ListsTheBuiltInAgentNotConnectedYet()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        var agents = await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/agents");

        var agent = Assert.Single(agents!);
        Assert.Equal("Built in", agent.GetProperty("name").GetString());
        Assert.Equal("BuiltIn", agent.GetProperty("placement").GetString());
        Assert.Equal("NotConnected", agent.GetProperty("state").GetString());
        Assert.True(File.Exists(Path.Combine(_directory, BuiltInAgentSetup.FileName)));
    }

    [Fact]
    public async Task SqlServers_AddChangeRemove_NeverSendsThePasswordBack()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var created = await admin.PostAsJsonAsync("/api/v1/sql-servers", Server("Correct-Horse-7"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var text = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Correct-Horse-7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        var server = JsonDocument.Parse(text).RootElement;
        Assert.Equal("Built in", server.GetProperty("agentName").GetString());
        Assert.Equal("AgentNotConnected", server.GetProperty("state").GetString());
        Assert.Equal("TrustServerCertificate", server.GetProperty("encryption").GetString());
        var id = server.GetProperty("id").GetString();

        using var changed = await admin.PutAsJsonAsync($"/api/v1/sql-servers/{id}", Server(null) with { Port = 1500 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var listed = Assert.Single((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/sql-servers"))!);
        Assert.Equal(1500, listed.GetProperty("port").GetInt32());

        using var removed = await admin.DeleteAsync(new Uri($"/api/v1/sql-servers/{id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/sql-servers"))!);
    }

    [Fact]
    public async Task SqlServers_AzureSqlDatabase_PlatformAndDatabaseComeBack()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var created = await admin.PostAsJsonAsync(
            "/api/v1/sql-servers",
            Server("Correct-Horse-7") with { Platform = "AzureSqlDatabase", Database = "orders" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var listed = Assert.Single((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/sql-servers"))!);
        Assert.Equal("AzureSqlDatabase", listed.GetProperty("platform").GetString());
        Assert.Equal("orders", listed.GetProperty("database").GetString());
    }

    [Fact]
    public async Task SqlServers_AzureSqlDatabaseWithoutDatabase_IsABadRequest()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync(
            "/api/v1/sql-servers",
            Server("Correct-Horse-7") with { Platform = "AzureSqlDatabase" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SqlServers_BadInput_IsABadRequestWithTheReason()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync("/api/v1/sql-servers", Server("x") with { Port = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("1 to 65535", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqlServers_Viewer_IsForbidden()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        using var user = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "viewer@example.com", displayName = "Viewer", role = "Viewer", password = _password });
        user.EnsureSuccessStatusCode();
        using var viewer = await factory.CreateSignedInClientAsync("viewer@example.com", _password);

        using var response = await viewer.GetAsync(new Uri("/api/v1/sql-servers", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OpAmp_WithoutKey_IsUnauthorized()
    {
        await using var factory = await CreateAsync();
        using var client = factory.CreateHttpsClient();

        using var poll = Poll(key: null);
        using var response = await client.SendAsync(poll);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OpAmp_NotProtobuf_IsUnsupported()
    {
        await using var factory = await CreateAsync();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync("/v1/opamp", new { instanceUid = "x" });

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task OpAmp_BuiltInAgent_GetsItsServersConfigurationAndShowsOnline()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        using var created = await admin.PostAsJsonAsync("/api/v1/sql-servers", Server("Correct-Horse-7"));
        created.EnsureSuccessStatusCode();
        var key = BuiltInAgentSetup.ReadKey(
            await File.ReadAllTextAsync(Path.Combine(_directory, BuiltInAgentSetup.FileName)));
        using var agent = factory.CreateHttpsClient();

        using var poll = Poll(key);
        using var response = await agent.SendAsync(poll);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);
        var reply = ServerToAgent.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());
        var config = reply.RemoteConfig.Config.ConfigMap[""].Body.ToStringUtf8();
        Assert.Contains("veyra_monitor:Correct-Horse-7@10.0.0.5:1433", config, StringComparison.Ordinal);
        Assert.Contains("endpoint: \"https://localhost\"", config, StringComparison.Ordinal);
        Assert.Equal("https://localhost/v1/logs", reply.ConnectionSettings.OwnLogs.DestinationEndpoint);

        var listed = Assert.Single((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/agents"))!);
        Assert.Equal("Online", listed.GetProperty("state").GetString());
        Assert.Equal("0.140.1", listed.GetProperty("version").GetString());
        Assert.Equal(1, listed.GetProperty("servers").GetInt32());
        var server = Assert.Single((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/sql-servers"))!);
        Assert.Equal("WaitingForData", server.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Host_WindowsAgent_IsSavedShownAndSentToTheAgent()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var (id, key) = await InstallAgentAsync(factory, admin, "Windows Server 2022 Datacenter");

        using var saved = await admin.PutAsJsonAsync(
            $"/api/v1/agents/{id}/host",
            new { host = true, eventLog = true, iis = true, environment = "staging" });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var agent = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement;
        Assert.True(agent.GetProperty("host").GetBoolean());
        Assert.True(agent.GetProperty("eventLog").GetBoolean());
        Assert.True(agent.GetProperty("iis").GetBoolean());
        Assert.Equal("staging", agent.GetProperty("hostEnvironment").GetString());
        var listed = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/agents"))!
            .Single(one => one.GetProperty("id").GetString() == id);
        Assert.True(listed.GetProperty("iis").GetBoolean());

        using var agentClient = factory.CreateHttpsClient();
        using var poll = Poll(key, "Windows Server 2022 Datacenter");
        using var response = await agentClient.SendAsync(poll);
        var reply = ServerToAgent.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("staging", reply.RemoteConfig.Config.ConfigMap[""].Body.ToStringUtf8(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_LinuxAgentWithDocker_IsSavedShownAndSentToTheAgent()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var (id, key) = await InstallAgentAsync(factory, admin, "Ubuntu 24.04.1 LTS");

        using var saved = await admin.PutAsJsonAsync(
            $"/api/v1/agents/{id}/host",
            new { host = true, eventLog = false, iis = false, docker = true, environment = "production" });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var agent = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement;
        Assert.True(agent.GetProperty("docker").GetBoolean());
        var listed = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/agents"))!
            .Single(one => one.GetProperty("id").GetString() == id);
        Assert.True(listed.GetProperty("docker").GetBoolean());

        using var agentClient = factory.CreateHttpsClient();
        using var poll = Poll(key, "Ubuntu 24.04.1 LTS");
        using var response = await agentClient.SendAsync(poll);
        var reply = ServerToAgent.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains(
            "docker_stats:",
            reply.RemoteConfig.Config.ConfigMap[""].Body.ToStringUtf8(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_DockerOnWindows_IsABadRequest()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var (id, _) = await InstallAgentAsync(factory, admin, "Windows Server 2022 Datacenter");

        using var response = await admin.PutAsJsonAsync(
            $"/api/v1/agents/{id}/host",
            new { host = true, eventLog = false, iis = false, docker = true, environment = "production" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Linux only", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_BuiltInAgent_IsABadRequest()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var builtIn = Assert.Single((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/agents"))!);

        using var response = await admin.PutAsJsonAsync(
            $"/api/v1/agents/{builtIn.GetProperty("id").GetString()}/host",
            new { host = true, eventLog = false, iis = false, environment = "production" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("runs in a container", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_EventLogOnLinux_IsABadRequest()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var (id, _) = await InstallAgentAsync(factory, admin, "Ubuntu 24.04.1 LTS");

        using var response = await admin.PutAsJsonAsync(
            $"/api/v1/agents/{id}/host",
            new { host = true, eventLog = true, iis = false, environment = "production" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Windows only", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_UnknownAgent_IsNotFound()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PutAsJsonAsync(
            $"/api/v1/agents/{Guid.NewGuid()}/host",
            new { host = true, eventLog = false, iis = false, environment = "production" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Editor")]
    public async Task Host_ViewerOrEditor_IsForbidden(string role)
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        using var user = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "user@example.com", displayName = "User", role, password = _password });
        user.EnsureSuccessStatusCode();
        using var client = await factory.CreateSignedInClientAsync("user@example.com", _password);

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/agents/{Guid.NewGuid()}/host",
            new { host = true, eventLog = false, iis = false, environment = "production" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private Task<VeyraApiFactory> CreateAsync() =>
        VeyraApiFactory.CreateAsync(postgres, ("Veyra:Agents:BuiltInDirectory", _directory));

    private static ServerInput Server(string? password) =>
        new(
            Name: "sql-prod-01",
            Address: "10.0.0.5",
            Port: 1433,
            Environment: "production",
            AgentId: null,
            Login: "veyra_monitor",
            Password: password,
            Encryption: "TrustServerCertificate");

    // Adds an agent, installs it with its code, and lets it report its operating system, as its Collector does.
    private static async Task<(string Id, string Key)> InstallAgentAsync(
        VeyraApiFactory factory,
        HttpClient admin,
        string operatingSystem)
    {
        using var added = await admin.PostAsJsonAsync("/api/v1/agents", new { name = "Web server" });
        added.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await added.Content.ReadAsStringAsync()).RootElement;
        var id = body.GetProperty("agent").GetProperty("id").GetString()!;

        using var installer = factory.CreateHttpsClient();
        using var enrolled = await installer.PostAsJsonAsync(
            "/v1/agents/enroll",
            new { code = body.GetProperty("code").GetString(), platform = "Windows" });
        enrolled.EnsureSuccessStatusCode();
        var key = Regex.Match(await enrolled.Content.ReadAsStringAsync(), "Bearer (vyk_[A-Za-z0-9_-]{43})")
            .Groups[1].Value;

        using var poll = Poll(key, operatingSystem);
        using var reported = await installer.SendAsync(poll);
        reported.EnsureSuccessStatusCode();

        return (id, key);
    }

    private static HttpRequestMessage Poll(string? key, string? operatingSystem = null)
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

        if (operatingSystem is not null)
        {
            message.AgentDescription.NonIdentifyingAttributes.Add(
                new KeyValue { Key = "os.description", Value = new AnyValue { StringValue = operatingSystem } });
        }


        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/opamp", UriKind.Relative))
        {
            Content = new ByteArrayContent(message.ToByteArray()),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        return request;
    }

    private sealed record ServerInput(
        string Name,
        string Address,
        int Port,
        string Environment,
        Guid? AgentId,
        string Login,
        string? Password,
        string Encryption,
        string Platform = "SqlServer",
        string? Database = null);
}
