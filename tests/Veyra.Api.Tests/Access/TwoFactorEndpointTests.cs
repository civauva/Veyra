using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Veyra.Access.SecondFactor;
using Veyra.Testing;

namespace Veyra.Api.Tests.Access;

/// <summary>Two-factor sign-in, sessions, and person details over HTTP (ADR 0011).</summary>
[Collection(PostgresGroup.Name)]
public class TwoFactorEndpointTests(PostgresFixture postgres)
{
    private const string _password = "another long secret";

    [Fact]
    public async Task SignIn_WithSecondFactor_NeedsACodeAndAcceptsARecoveryCode()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var codes = await SwitchOnAsync(admin);
        using var client = factory.CreateHttpsClient();

        using var first = await client.PostAsJsonAsync(
            "/api/v1/session",
            new { email = VeyraApiFactory.AdminEmail, password = VeyraApiFactory.AdminPassword });
        var next = await first.Content.ReadFromJsonAsync<JsonElement>();
        using var before = await client.GetAsync(new Uri("/api/v1/session", UriKind.Relative));
        using var second = await client.PostAsJsonAsync("/api/v1/session/second-factor", new { code = codes[0] });
        using var after = await client.GetAsync(new Uri("/api/v1/session", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal("code", next.GetProperty("next").GetString());
        var pending = Assert.Single(
            first.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("veyra.pending=",
            StringComparison.Ordinal));
        Assert.Contains("httponly", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task SecondFactor_WrongCode_IsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SwitchOnAsync(admin);
        using var client = factory.CreateHttpsClient();
        using var first = await client.PostAsJsonAsync(
            "/api/v1/session",
            new { email = VeyraApiFactory.AdminEmail, password = VeyraApiFactory.AdminPassword });

        using var wrong = await client.PostAsJsonAsync("/api/v1/session/second-factor", new { code = "ZZZZ-ZZZZ" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task SecondFactor_WithoutThePasswordStep_IsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync("/api/v1/session/second-factor", new { code = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EndOtherSessions_SignsTheOtherBrowserOutAtOnce()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var laptop = await factory.CreateSignedInClientAsync(
            VeyraApiFactory.AdminEmail,
            VeyraApiFactory.AdminPassword);

        var account = await admin.GetFromJsonAsync<JsonElement>("/api/v1/account");
        using var ended = await admin.PostAsync(new Uri("/api/v1/account/sessions/end-others", UriKind.Relative), null);
        using var laptopAfter = await laptop.GetAsync(new Uri("/api/v1/session", UriKind.Relative));
        using var adminAfter = await admin.GetAsync(new Uri("/api/v1/session", UriKind.Relative));

        Assert.Equal(2, account.GetProperty("sessions").GetArrayLength());
        Assert.Single(account.GetProperty("sessions").EnumerateArray(), one => one.GetProperty("current").GetBoolean());
        Assert.Equal(1, (await ended.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ended").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, laptopAfter.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminAfter.StatusCode);
    }

    [Fact]
    public async Task SignIn_RequiredForEveryone_SetsUpTheSecondFactorThenSignsIn()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await CreateViewerAsync(admin);
        using var policy = await admin.PutAsJsonAsync(
            "/api/v1/users/second-factor-policy",
            new { requiredFor = "Everyone" });
        using var client = factory.CreateHttpsClient();

        using var first = await client.PostAsJsonAsync(
            "/api/v1/session",
            new { email = "mei@example.com", password = _password });
        using var setup = await client.PostAsync(
            new Uri("/api/v1/session/second-factor/setup", UriKind.Relative),
            null);
        var secret = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
        using var confirm = await client.PostAsJsonAsync(
            "/api/v1/session/second-factor/setup/confirm",
            new { code = Totp.Code(Base32.Decode(secret), Totp.StepAt(DateTimeOffset.UtcNow)) });
        var confirmed = await confirm.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        Assert.Equal("setup", (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("next").GetString());
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(10, confirmed.GetProperty("recoveryCodes").GetArrayLength());
        Assert.Equal("mei@example.com", confirmed.GetProperty("session").GetProperty("email").GetString());
        var account = await client.GetFromJsonAsync<JsonElement>("/api/v1/account");
        Assert.Equal(JsonValueKind.Object, account.GetProperty("secondFactor").ValueKind);
    }

    [Fact]
    public async Task UserDetail_ShowsTeamSignInsAndWhoAddedThePerson()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var id = await CreateViewerAsync(admin);
        using var wrong = await factory.CreateHttpsClient().PostAsJsonAsync(
            "/api/v1/session",
            new { email = "mei@example.com", password = "wrong password!" });

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/users/{id}");

        Assert.Equal("Data", detail.GetProperty("user").GetProperty("team").GetString());
        Assert.Equal("Root", detail.GetProperty("addedBy").GetString());
        var attempt = Assert.Single(detail.GetProperty("signIns").EnumerateArray());
        Assert.Equal("WrongPassword", attempt.GetProperty("outcome").GetString());
        Assert.Equal(1, detail.GetProperty("failedLast30Days").GetInt32());
        Assert.Equal(
            "Added",
            Assert.Single(detail.GetProperty("events").EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ResetPassword_ByAnAdmin_LetsTheNewPasswordSignIn()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var id = await CreateViewerAsync(admin);

        using var reset = await admin.PostAsJsonAsync(
            $"/api/v1/users/{id}/password",
            new { password = "copper tide amber sketch" });
        using var client = factory.CreateHttpsClient();
        using var signIn = await client.PostAsJsonAsync(
            "/api/v1/session",
            new { email = "mei@example.com", password = "copper tide amber sketch" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    private static async Task<Guid> CreateViewerAsync(HttpClient admin)
    {
        using var created = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email = "mei@example.com",
                displayName = "Mei Chen",
                role = "Viewer",
                password = _password,
                team = "Data",
            });
        created.EnsureSuccessStatusCode();

        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<IReadOnlyList<string>> SwitchOnAsync(HttpClient admin)
    {
        using var start = await admin.PostAsync(new Uri("/api/v1/account/second-factor", UriKind.Relative), null);
        start.EnsureSuccessStatusCode();
        var secret = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;

        using var confirm = await admin.PostAsJsonAsync(
            "/api/v1/account/second-factor/confirm",
            new { code = Totp.Code(Base32.Decode(secret), Totp.StepAt(DateTimeOffset.UtcNow)) });
        confirm.EnsureSuccessStatusCode();

        var body = await confirm.Content.ReadFromJsonAsync<JsonElement>();

        return [.. body.GetProperty("recoveryCodes").EnumerateArray().Select(one => one.GetString()!)];
    }
}
