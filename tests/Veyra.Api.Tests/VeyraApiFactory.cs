using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Access.Setup;
using Veyra.Testing;

namespace Veyra.Api.Tests;

/// <summary>
/// Hosts the whole Veyra API against its own new PostgreSQL database, so a test sees exactly the startup a real
/// installation goes through: migrations, then first-run setup.
/// </summary>
internal sealed class VeyraApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string AdminEmail = "root@example.com";

    public const string AdminPassword = "correct horse battery";

    /// <summary>Creates a factory with a fresh, empty database.</summary>
    public static async Task<VeyraApiFactory> CreateAsync(PostgresFixture postgres) =>
        new(await postgres.CreateDatabaseConnectionStringAsync());

    /// <summary>
    /// Returns a client that talks HTTPS, so the <c>Secure</c> session cookie behaves as in production.
    /// </summary>
    public HttpClient CreateHttpsClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    /// <summary>Completes first-run setup through the API and returns a client signed in as that admin.</summary>
    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var token = Services.GetRequiredService<SetupService>().Token
            ?? throw new InvalidOperationException("Setup is not open; was the database not empty?");

        using var anonymous = CreateHttpsClient();
        using var setup = await anonymous.PostAsJsonAsync(
            "/api/v1/setup",
            new { token, email = AdminEmail, displayName = "Root", password = AdminPassword });
        setup.EnsureSuccessStatusCode();

        return await CreateSignedInClientAsync(AdminEmail, AdminPassword);
    }

    /// <summary>Returns a client signed in as the given user, with the antiforgery header set.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(string email, string password)
    {
        var client = CreateHttpsClient();

        using var signIn = await client.PostAsJsonAsync("/api/v1/session", new { email, password });
        signIn.EnsureSuccessStatusCode();

        var antiforgery = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/v1/session/antiforgery");
        client.DefaultRequestHeaders.Add("X-Veyra-Antiforgery", antiforgery!.Token);

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Veyra:Storage:ConnectionString", connectionString);

        // Tests run the alert rules themselves, when their records are in place. The background run at startup
        // finds nothing; a later one must not race a test's own run.
        builder.UseSetting("Veyra:Alerting:EvaluationInterval", "01:00:00");
    }

    private sealed record AntiforgeryResponse(string Token);
}
