using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Veyra.Testing;

namespace Veyra.Api.Tests.Access;

/// <summary>Saved views of the Log explorer over the API (ADR 0024).</summary>
[Collection(PostgresGroup.Name)]
public class ViewsEndpointTests(PostgresFixture postgres)
{
    private const string _path = "/api/v1/views";

    private const string _password = "another long secret";

    [Fact]
    public async Task GetViews_Anonymous_ReturnsUnauthorized()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateHttpsClient();

        using var response = await anonymous.GetAsync(new Uri($"{_path}?screen=logs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostView_Viewer_IsSavedAndListedAsTheirs()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var ana = await PersonAsync(factory, admin, "ana@example.com", "Ana");

        using var created = await ana.PostAsJsonAsync(
            _path,
            new { screen = "logs", name = "Slow payments", query = "service:payments level>=warn", shared = false });
        var view = await created.Content.ReadFromJsonAsync<JsonElement>();
        var list = await ana.GetFromJsonAsync<JsonElement>($"{_path}?screen=logs");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/v1/views/{view.GetProperty("id").GetGuid()}", created.Headers.Location?.ToString());
        var listed = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(
            ("logs", "Slow payments", "service:payments level>=warn", false, true, "Ana"),
            (listed.GetProperty("screen").GetString(),
                listed.GetProperty("name").GetString(),
                listed.GetProperty("query").GetString(),
                listed.GetProperty("shared").GetBoolean(),
                listed.GetProperty("mine").GetBoolean(),
                listed.GetProperty("ownerName").GetString()));
        Assert.True(listed.TryGetProperty("createdAt", out _));
        Assert.True(listed.TryGetProperty("updatedAt", out _));
    }

    [Fact]
    public async Task GetViews_OthersSharedView_IsListedWithWhoSharedItAndCannotBeChanged()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var ana = await PersonAsync(factory, admin, "ana@example.com", "Ana");
        var shared = await SaveAsync(admin, "Errors", "level:error", shared: true);
        var hidden = await SaveAsync(admin, "Mine alone", "", shared: false);

        var list = await ana.GetFromJsonAsync<JsonElement>($"{_path}?screen=logs");
        using var change = await ana.PutAsJsonAsync($"{_path}/{shared}", new { name = "Taken", query = "", shared = true });
        using var delete = await ana.DeleteAsync(new Uri($"{_path}/{shared}", UriKind.Relative));
        using var changeHidden = await ana.PutAsJsonAsync($"{_path}/{hidden}", new { name = "Found", shared = false });
        using var deleteHidden = await ana.DeleteAsync(new Uri($"{_path}/{hidden}", UriKind.Relative));

        var listed = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(
            ("Errors", false, "Root"),
            (listed.GetProperty("name").GetString(),
                listed.GetProperty("mine").GetBoolean(),
                listed.GetProperty("ownerName").GetString()));
        Assert.Equal(
            (HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound),
            (change.StatusCode, delete.StatusCode, changeHidden.StatusCode, deleteHidden.StatusCode));
    }

    [Fact]
    public async Task PutAndDeleteView_Owner_ChangesAndRemovesIt()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var id = await SaveAsync(admin, "Errors", "level:error", shared: false);

        using var change = await admin.PutAsJsonAsync(
            $"{_path}/{id}",
            new { name = "Fatal", query = "level:fatal", shared = true });
        var changed = await change.Content.ReadFromJsonAsync<JsonElement>();
        using var delete = await admin.DeleteAsync(new Uri($"{_path}/{id}", UriKind.Relative));
        var list = await admin.GetFromJsonAsync<JsonElement>($"{_path}?screen=logs");

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(
            ("Fatal", "level:fatal", true),
            (changed.GetProperty("name").GetString(),
                changed.GetProperty("query").GetString(),
                changed.GetProperty("shared").GetBoolean()));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(list.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("logs", "", "")]
    [InlineData("traces", "Name", "")]
    [InlineData(null, "Name", "")]
    public async Task PostView_BadValues_IsABadRequestThatSaysWhy(string? screen, string name, string query)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync(_path, new { screen, name, query, shared = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()));
    }

    [Fact]
    public async Task PostView_TooLongName_IsABadRequest()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync(
            _path,
            new { screen = "logs", name = new string('n', 81), query = "", shared = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetViews_UnknownScreen_IsABadRequest()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri($"{_path}?screen=home", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostView_HundredAlready_IsAConflict()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        for (var number = 0; number < 100; number++)
        {
            await SaveAsync(admin, $"View {number}", "", shared: false);
        }

        using var response = await admin.PostAsJsonAsync(
            _path,
            new { screen = "logs", name = "One more", query = "", shared = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<Guid> SaveAsync(HttpClient client, string name, string query, bool shared)
    {
        using var response = await client.PostAsJsonAsync(_path, new { screen = "logs", name, query, shared });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<HttpClient> PersonAsync(
        VeyraApiFactory factory,
        HttpClient admin,
        string email,
        string displayName)
    {
        using var created = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email, displayName, role = "Viewer", password = _password });
        created.EnsureSuccessStatusCode();

        return await factory.CreateSignedInClientAsync(email, _password);
    }
}
