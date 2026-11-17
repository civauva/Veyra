using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Veyra.Testing;

namespace Veyra.Api.Tests.Agents;

[Collection(PostgresGroup.Name)]
public sealed class PostgresServersEndpointTests(PostgresFixture postgres) : IDisposable
{
    private const string _password = "a long enough password";

    private readonly string _directory = Directory.CreateTempSubdirectory("veyra-postgres-api-").FullName;

    [Fact]
    public async Task PostgresServers_AddChangeRemove_NeverSendsThePasswordBack()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var created = await admin.PostAsJsonAsync("/api/v1/postgres-servers", Server("Correct-Horse-7"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.StartsWith(
            "/api/v1/postgres-servers/",
            created.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
        var text = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Correct-Horse-7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        var server = JsonDocument.Parse(text).RootElement;
        Assert.Equal("pg-main", server.GetProperty("name").GetString());
        Assert.Equal("Built in", server.GetProperty("agentName").GetString());
        Assert.Equal("AgentNotConnected", server.GetProperty("state").GetString());
        Assert.Equal("TrustServerCertificate", server.GetProperty("encryption").GetString());
        var id = server.GetProperty("id").GetString();

        using var changed = await admin.PutAsJsonAsync(
            $"/api/v1/postgres-servers/{id}",
            Server(null) with { Port = 5433 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var listed = Assert.Single((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/postgres-servers"))!);
        Assert.Equal(5433, listed.GetProperty("port").GetInt32());

        var agent = Assert.Single((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/agents"))!);
        Assert.Equal(1, agent.GetProperty("servers").GetInt32());

        using var removed = await admin.DeleteAsync(new Uri($"/api/v1/postgres-servers/{id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/postgres-servers"))!);
    }

    [Fact]
    public async Task PostgresServers_Changes_AreInTheSettingsHistoryOfDatabaseMonitoring()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        var version = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/settings")).GetProperty("version").GetInt64();

        using var created = await admin.PostAsJsonAsync("/api/v1/postgres-servers", Server("Correct-Horse-7"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        using var changed = await admin.PutAsJsonAsync($"/api/v1/postgres-servers/{id}", Server("Battery-Staple-9") with { Port = 5433 });
        using var removed = await admin.DeleteAsync(new Uri($"/api/v1/postgres-servers/{id}", UriKind.Relative));

        var text = await admin.GetStringAsync("/api/v1/settings/history?section=Databases");
        var changes = JsonDocument.Parse(text).RootElement.GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(
            ["Removed", "Changed", "Added"],
            changes.Select(change => change.GetProperty("kind").GetString()));
        Assert.All(changes, change =>
        {
            Assert.Equal("pg-main", change.GetProperty("label").GetString());
            Assert.Equal("PostgreSQL server", change.GetProperty("group").GetString());
            Assert.Equal("Databases", change.GetProperty("section").GetString());
            Assert.Equal("PostgresServer", change.GetProperty("subject").GetString());
            Assert.False(change.GetProperty("canRevert").GetBoolean());
        });
        Assert.Equal(
            ["port", "password"],
            changes[1].GetProperty("fields").EnumerateArray().Select(field => field.GetString()));
        Assert.DoesNotContain("Horse", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Staple", text, StringComparison.Ordinal);

        Assert.Empty(JsonDocument.Parse(await admin.GetStringAsync("/api/v1/settings/history?section=Data"))
            .RootElement.GetProperty("changes").EnumerateArray());
        Assert.Equal(
            version,
            (await admin.GetFromJsonAsync<JsonElement>("/api/v1/settings")).GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task PostgresServers_AreNotSqlServers()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var created = await admin.PostAsJsonAsync("/api/v1/postgres-servers", Server("Correct-Horse-7"));
        created.EnsureSuccessStatusCode();

        Assert.Empty((await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/sql-servers"))!);
    }

    [Fact]
    public async Task PostgresServers_BadInput_IsABadRequestWithTheReason()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync(
            "/api/v1/postgres-servers",
            Server("two words"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("spaces, quotes", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgresServers_NameInUse_IsAConflict()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        using var first = await admin.PostAsJsonAsync("/api/v1/postgres-servers", Server("Correct-Horse-7"));
        first.EnsureSuccessStatusCode();

        using var second = await admin.PostAsJsonAsync("/api/v1/postgres-servers", Server("Correct-Horse-7"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PostgresServers_UnknownId_IsNotFound()
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();

        using var changed = await admin.PutAsJsonAsync($"/api/v1/postgres-servers/{Guid.NewGuid()}", Server(null));
        using var removed = await admin.DeleteAsync(
            new Uri($"/api/v1/postgres-servers/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, changed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Editor")]
    public async Task PostgresServers_ViewerOrEditor_IsForbidden(string role)
    {
        await using var factory = await CreateAsync();
        using var admin = await factory.CreateAdminClientAsync();
        using var user = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "user@example.com", displayName = "User", role, password = _password });
        user.EnsureSuccessStatusCode();
        using var client = await factory.CreateSignedInClientAsync("user@example.com", _password);

        using var response = await client.GetAsync(new Uri("/api/v1/postgres-servers", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private Task<VeyraApiFactory> CreateAsync() =>
        VeyraApiFactory.CreateAsync(postgres, ("Veyra:Agents:BuiltInDirectory", _directory));

    private static ServerInput Server(string? password) =>
        new(
            Name: "pg-main",
            Address: "10.0.0.5",
            Port: 5432,
            Environment: "production",
            AgentId: null,
            Login: "veyra_monitor",
            Password: password,
            Encryption: "TrustServerCertificate");

    private sealed record ServerInput(
        string Name,
        string Address,
        int Port,
        string Environment,
        Guid? AgentId,
        string Login,
        string? Password,
        string Encryption);
}
