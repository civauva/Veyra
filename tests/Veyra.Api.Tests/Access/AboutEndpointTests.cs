using System.Net;
using System.Text.Json;
using Veyra.Testing;

namespace Veyra.Api.Tests.Access;

/// <summary>The running version, its build, and the release notes, for everyone signed in (VEY-82).</summary>
[Collection(PostgresGroup.Name)]
public class AboutEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task About_SignedIn_IsTheVersionAndItsBuild()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var about = await JsonAsync(admin, "/api/v1/about");

        Assert.Matches(@"^\d+\.\d+\.\d+$", about.RootElement.GetProperty("version").GetString());
        Assert.False(string.IsNullOrEmpty(about.RootElement.GetProperty("build").GetString()));
    }

    [Fact]
    public async Task Releases_SignedIn_StartWithTheRunningVersion()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var about = await JsonAsync(admin, "/api/v1/about");

        using var releases = await JsonAsync(admin, "/api/v1/about/releases");

        var latest = releases.RootElement[0];
        Assert.Equal(about.RootElement.GetProperty("version").GetString(), latest.GetProperty("version").GetString());
        Assert.NotEqual(0, latest.GetProperty("sections").GetArrayLength());
    }

    [Theory]
    [InlineData("/api/v1/about")]
    [InlineData("/api/v1/about/releases")]
    public async Task About_Anonymous_IsUnauthorized(string path)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<JsonDocument> JsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
