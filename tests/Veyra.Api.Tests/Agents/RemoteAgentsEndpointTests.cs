using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Veyra.Testing;

namespace Veyra.Api.Tests.Agents;

[Collection(PostgresGroup.Name)]
public class RemoteAgentsEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task AddAndEnroll_Code_GivesTheInstallerTheStartupFileAndThePageAnInstalledAgent()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var added = await admin.PostAsJsonAsync("/api/v1/agents", new { name = "Company network" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var body = JsonDocument.Parse(await added.Content.ReadAsStringAsync()).RootElement;
        var code = body.GetProperty("code").GetString();
        Assert.False(body.GetProperty("agent").GetProperty("installed").GetBoolean());
        Assert.Equal("Remote", body.GetProperty("agent").GetProperty("placement").GetString());

        using var installer = factory.CreateHttpsClient();
        using var enrolled = await installer.PostAsJsonAsync("/v1/agents/enroll", new { code, platform = "Windows" });

        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        Assert.Equal("text/yaml", enrolled.Content.Headers.ContentType?.MediaType);
        var startup = await enrolled.Content.ReadAsStringAsync();
        Assert.Contains("endpoint: https://localhost/v1/opamp", startup, StringComparison.Ordinal);
        Assert.Contains(
            @"executable: 'C:\Program Files\Veyra Agent\otelcol-contrib.exe'",
            startup,
            StringComparison.Ordinal);
        Assert.Matches("Authorization: Bearer vyk_[A-Za-z0-9_-]{43}", startup);

        var agent = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/agents"))!
            .Single(one => one.GetProperty("name").GetString() == "Company network");
        Assert.True(agent.GetProperty("installed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, agent.GetProperty("codeExpiresAt").ValueKind);
    }

    [Fact]
    public async Task Enroll_UsedCode_IsForbidden()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var code = await AddAsync(admin);
        using var installer = factory.CreateHttpsClient();
        using var first = await installer.PostAsJsonAsync("/v1/agents/enroll", new { code, platform = "Linux" });

        using var second = await installer.PostAsJsonAsync("/v1/agents/enroll", new { code, platform = "Linux" });

        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [Fact]
    public async Task Enroll_ManyWrongCodes_AreThrottled()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var installer = factory.CreateHttpsClient();

        for (var i = 0; i < 10; i++)
        {
            using var wrong = await installer.PostAsJsonAsync(
                "/v1/agents/enroll",
                new { code = "AAAA-BBBB-CCCC", platform = "Linux" });
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        }

        using var blocked = await installer.PostAsJsonAsync(
            "/v1/agents/enroll",
            new { code = "AAAA-BBBB-CCCC", platform = "Linux" });

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task Enroll_UnknownPlatform_IsABadRequest()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var installer = factory.CreateHttpsClient();

        using var response = await installer.PostAsJsonAsync(
            "/v1/agents/enroll",
            new { code = "AAAA-BBBB-CCCC", platform = "Container" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/v1/agents/install.ps1", "$VeyraUrl = 'https://localhost'")]
    [InlineData("/v1/agents/install.sh", "VEYRA_URL='https://localhost'")]
    public async Task Installers_AreServedToAnyoneWithVeyrasAddress(string path, string line)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        var script = await anonymous.GetStringAsync(new Uri(path, UriKind.Relative));

        Assert.Contains(line, script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewCodeAndRemove_RemoteAgent_WorkForAdmins()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var added = await admin.PostAsJsonAsync("/api/v1/agents", new { name = "Company network" });
        var id = JsonDocument.Parse(await added.Content.ReadAsStringAsync()).RootElement
            .GetProperty("agent").GetProperty("id").GetString();

        using var code = await admin.PostAsync(new Uri($"/api/v1/agents/{id}/code", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, code.StatusCode);
        Assert.Matches("\"code\":\"[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}\"", await code.Content.ReadAsStringAsync());

        using var removed = await admin.DeleteAsync(new Uri($"/api/v1/agents/{id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.DoesNotContain(
            (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/agents"))!,
            agent => agent.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task Add_EmptyName_IsABadRequest()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync("/api/v1/agents", new { name = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string> AddAsync(HttpClient admin)
    {
        using var added = await admin.PostAsJsonAsync("/api/v1/agents", new { name = "Company network" });
        added.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await added.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;
    }
}
