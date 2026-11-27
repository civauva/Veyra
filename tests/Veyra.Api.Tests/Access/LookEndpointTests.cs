using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Veyra.Testing;

namespace Veyra.Api.Tests.Access;

/// <summary>A look of the app per person, and the instance default an Admin sets (VEY-69, ADR 0021).</summary>
[Collection(PostgresGroup.Name)]
public class LookEndpointTests(PostgresFixture postgres)
{
    private const string _path = "/api/v1/account/look";

    [Fact]
    public async Task GetSession_NothingChosen_HasTheDefaultLookAndMayChoose()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        var look = await LookAsync(admin);

        Assert.Equal(JsonValueKind.Null, look.GetProperty("chosen").ValueKind);
        Assert.Equal("console", look.GetProperty("default").GetString());
        Assert.True(look.GetProperty("mayChoose").GetBoolean());
    }

    [Fact]
    public async Task PutLook_KnownLook_IsKeptOnTheSession()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var put = await admin.PutAsJsonAsync(_path, new { look = "console" });

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal("console", (await LookAsync(admin)).GetProperty("chosen").GetString());
    }

    [Fact]
    public async Task PutLook_UnknownLook_IsRefusedAndKeepsTheOldOne()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var first = await admin.PutAsJsonAsync(_path, new { look = "console" });

        using var wrong = await admin.PutAsJsonAsync(_path, new { look = "city" });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("The look must be one of: forecast, console.", await TitleAsync(wrong));
        Assert.Equal("console", (await LookAsync(admin)).GetProperty("chosen").GetString());
    }

    [Fact]
    public async Task PutLook_Null_ClearsTheChoice()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var first = await admin.PutAsJsonAsync(_path, new { look = "console" });

        using var cleared = await admin.PutAsJsonAsync(_path, new { look = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await LookAsync(admin)).GetProperty("chosen").ValueKind);
    }

    [Fact]
    public async Task PutLook_PeopleMayNotChoose_IsForbiddenAndTheSessionKeepsTheSavedLook()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var first = await admin.PutAsJsonAsync(_path, new { look = "console" });
        await SaveAsync(admin, ("Veyra:Access:PeopleChooseLook", "false"), ("Veyra:Access:DefaultLook", "console"));

        using var refused = await admin.PutAsJsonAsync(_path, new { look = "forecast" });
        var look = await LookAsync(admin);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("An Admin has set one look for everyone.", await TitleAsync(refused));
        Assert.Equal(
            ("console", "console", false),
            (look.GetProperty("chosen").GetString(),
                look.GetProperty("default").GetString(),
                look.GetProperty("mayChoose").GetBoolean()));
    }

    [Fact]
    public async Task SignIn_Session_HasTheLook()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var created = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "ana@example.com", displayName = "Ana", role = "Viewer", password = "another long secret" });
        created.EnsureSuccessStatusCode();
        using var client = factory.CreateHttpsClient();

        using var signIn = await client.PostAsJsonAsync(
            "/api/v1/session",
            new { email = "ana@example.com", password = "another long secret" });

        signIn.EnsureSuccessStatusCode();
        var look = (await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("look");
        Assert.Equal("console", look.GetProperty("default").GetString());
        Assert.True(look.GetProperty("mayChoose").GetBoolean());
    }

    [Fact]
    public async Task GetSession_AdminSetForecast_KeepsForecastAsTheDefault()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SaveAsync(admin, ("Veyra:Access:DefaultLook", "forecast"));

        var look = await LookAsync(admin);

        Assert.Equal("forecast", look.GetProperty("default").GetString());
    }

    [Fact]
    public async Task SaveSetting_UnknownDefaultLook_IsRefused()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var version = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/settings")).GetProperty("version").GetInt64();

        using var response = await admin.PutAsJsonAsync(
            "/api/v1/settings",
            new
            {
                version,
                changes = new[] { new { key = "Veyra:Access:DefaultLook", value = "city" } },
                confirmed = true,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Veyra:Access:DefaultLook",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("problems")[0]
                .GetProperty("key")
                .GetString());
    }

    private static async Task<JsonElement> LookAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("look");

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();

    private static async Task SaveAsync(HttpClient admin, params (string Key, string Value)[] changes)
    {
        var version = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/settings")).GetProperty("version").GetInt64();
        using var response = await admin.PutAsJsonAsync(
            "/api/v1/settings",
            new
            {
                version,
                changes = changes.Select(change => new { key = change.Key, value = change.Value }),
                confirmed = true,
            });
        response.EnsureSuccessStatusCode();
    }
}
