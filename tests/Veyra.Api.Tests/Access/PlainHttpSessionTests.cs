using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Veyra.Testing;

namespace Veyra.Api.Tests.Access;

/// <summary>
/// Outside Development the session and antiforgery cookies are <c>Secure</c>, so signing in cannot work over plain
/// HTTP. These tests pin that Veyra says so in a problem response instead of failing with a 500 (VEY-16).
/// </summary>
[Collection(PostgresGroup.Name)]
public class PlainHttpSessionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SignIn_PlainHttp_ReturnsBadRequestThatAsksForHttps()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var http = factory.CreateClient();

        using var response = await http.PostAsJsonAsync(
            "/api/v1/session",
            new { email = VeyraApiFactory.AdminEmail, password = VeyraApiFactory.AdminPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("HTTPS", await ProblemTitleAsync(response), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task SignIn_PlainHttpWithWrongPassword_ReturnsTheHttpsProblemBeforeCheckingThePassword()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var http = factory.CreateClient();

        using var response = await http.PostAsJsonAsync(
            "/api/v1/session",
            new { email = VeyraApiFactory.AdminEmail, password = "wrong password!" });

        // A request that cannot succeed must not count towards the account's lockout.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("HTTPS", await ProblemTitleAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Antiforgery_PlainHttpWithSession_ReturnsBadRequestThatAsksForHttps()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var sessionCookie = await SignInOverHttpsAsync(factory);
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/session/antiforgery");
        request.Headers.Add("Cookie", sessionCookie);

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("HTTPS", await ProblemTitleAsync(response), StringComparison.Ordinal);
    }

    /// <summary>Signs in over HTTPS and returns the session cookie as a <c>name=value</c> pair.</summary>
    private static async Task<string> SignInOverHttpsAsync(VeyraApiFactory factory)
    {
        using var https = factory.CreateHttpsClient();
        using var signIn = await https.PostAsJsonAsync(
            "/api/v1/session",
            new { email = VeyraApiFactory.AdminEmail, password = VeyraApiFactory.AdminPassword });
        signIn.EnsureSuccessStatusCode();

        var cookie = signIn.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("veyra.session=", StringComparison.Ordinal));

        return cookie.Split(';')[0];
    }

    private static async Task<string> ProblemTitleAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.GetProperty("title").GetString() ?? "";
    }
}
