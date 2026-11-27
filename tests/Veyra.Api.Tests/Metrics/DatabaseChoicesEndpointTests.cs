using System.Net;
using Veyra.Testing;

namespace Veyra.Api.Tests.Metrics;

/// <summary>
/// A database server's screen sends every choice it offers as a parameter, and the server filters and ranks with it
/// (VEY-66). These prove the endpoints read them, and refuse a ranking they do not know.
/// </summary>
[Collection(PostgresGroup.Name)]
public class DatabaseChoicesEndpointTests(PostgresFixture postgres)
{
    private const string _server = "/api/v1/metrics/databases/microsoft.sql_server/sql-prod-01/";

    [Theory]
    [InlineData("?range=1h&system=true", HttpStatusCode.NotFound)]
    [InlineData("queries?range=1h&system=true&sort=timePerRun", HttpStatusCode.OK)]
    [InlineData("queries?sort=reads", HttpStatusCode.OK)]
    [InlineData("queries?sort=loudest", HttpStatusCode.BadRequest)]
    [InlineData("jobs?system=true", HttpStatusCode.OK)]
    [InlineData("indexes?system=false", HttpStatusCode.OK)]
    [InlineData("server?range=1h&system=true", HttpStatusCode.NotFound)]
    [InlineData("storage?system=true", HttpStatusCode.NotFound)]
    [InlineData("live?program=checkout-api&wait=LCK_M_U", HttpStatusCode.NotFound)]
    public async Task Get_ServerChoices_AreReadFromTheQuery(string path, HttpStatusCode expected)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(_server + path, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }
}
