using System.Net;
using System.Net.Http.Json;
using Veyra.Testing;

namespace Veyra.Api.Tests.Access;

[Collection(PostgresGroup.Name)]
public class AccessEndpointTests(PostgresFixture postgres)
{
    private const string _password = "another long secret";

    [Fact]
    public async Task Setup_FreshInstallation_IsRequiredUntilTheFirstAdminExists()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        var before = await anonymous.GetFromJsonAsync<SetupStatus>("/api/v1/setup");
        using var admin = await factory.CreateAdminClientAsync();
        var after = await anonymous.GetFromJsonAsync<SetupStatus>("/api/v1/setup");

        Assert.True(before!.Required);
        Assert.False(after!.Required);
    }

    [Fact]
    public async Task Setup_WrongToken_ReturnsBadRequest()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.PostAsJsonAsync(
            "/api/v1/setup",
            new { token = "guessed", email = "evil@example.com", displayName = "Evil", password = _password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Session_CorrectPassword_ReturnsTheUserWithAnHttpOnlySecureCookie()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/session",
            new { email = VeyraApiFactory.AdminEmail, password = VeyraApiFactory.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("veyra.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_WrongPassword_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/session",
            new { email = VeyraApiFactory.AdminEmail, password = "wrong password!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CurrentSession_SignedIn_ReturnsEmailAndRole()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        var session = await admin.GetFromJsonAsync<SessionInfo>("/api/v1/session");

        Assert.Equal(VeyraApiFactory.AdminEmail, session!.Email);
        Assert.Equal("Admin", session.Role);
    }

    [Fact]
    public async Task Users_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri("/api/v1/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_Viewer_ReturnsForbidden()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await CreateUserAsync(admin, "viewer@example.com", "Viewer");
        using var viewer = await factory.CreateSignedInClientAsync("viewer@example.com", _password);

        using var response = await viewer.GetAsync(new Uri("/api/v1/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Users_PostWithoutAntiforgeryHeader_ReturnsBadRequest()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        admin.DefaultRequestHeaders.Remove("X-Veyra-Antiforgery");

        using var response = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "ana@example.com", displayName = "Ana", role = "Viewer", password = _password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_DisablingAUser_EndsTheirExistingSession()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var created = await CreateUserAsync(admin, "ana@example.com", "Viewer");
        using var ana = await factory.CreateSignedInClientAsync("ana@example.com", _password);

        using var disable = await admin.PatchAsJsonAsync($"/api/v1/users/{created.Id}", new { disabled = true });
        using var response = await ana.GetAsync(new Uri("/api/v1/session", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IngestionKeys_Create_ReturnsTheSecretOnceAndNeverInTheList()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var create = await admin.PostAsJsonAsync("/api/v1/ingestion-keys", new { name = "web-01 collector" });
        var created = await create.Content.ReadFromJsonAsync<CreatedKey>();
        var listed = await admin.GetStringAsync(new Uri("/api/v1/ingestion-keys", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.StartsWith("vyk_", created!.Secret, StringComparison.Ordinal);
        Assert.DoesNotContain(created.Secret, listed, StringComparison.Ordinal);
        Assert.Contains(created.DisplayPrefix, listed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IngestionKeys_Revoke_ReturnsNoContent()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var create = await admin.PostAsJsonAsync("/api/v1/ingestion-keys", new { name = "api" });
        var created = await create.Content.ReadFromJsonAsync<CreatedKey>();

        using var revoke = await admin.DeleteAsync(new Uri($"/api/v1/ingestion-keys/{created!.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
    }

    private static async Task<UserInfo> CreateUserAsync(HttpClient admin, string email, string role)
    {
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email, displayName = email, role, password = _password });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UserInfo>())!;
    }

    private sealed record SetupStatus(bool Required);

    private sealed record SessionInfo(Guid Id, string Email, string DisplayName, string Role);

    private sealed record UserInfo(Guid Id, string Email, string Role, bool Disabled);

    private sealed record CreatedKey(Guid Id, string Name, string DisplayPrefix, string Secret);
}
