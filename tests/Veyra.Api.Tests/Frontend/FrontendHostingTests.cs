using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Veyra.Api.Tests.Frontend;

/// <summary>
/// The API serves the built frontend (ADR 0004): files from wwwroot, and index.html for the app's own routes.
/// </summary>
public sealed class FrontendHostingTests : IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("veyra-wwwroot-").FullName;
    private readonly WebApplicationFactory<Program> _factory;

    public FrontendHostingTests()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>Veyra</title>");
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app.js"), "console.log('veyra');");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/logs")]
    [InlineData("/keys")]
    public async Task AppRoute_Anonymous_ReturnsIndexHtml(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Veyra</title>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticFile_Anonymous_IsServed()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/assets/app.js", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("console.log('veyra');", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingFile_Anonymous_IsNotAnsweredWithTheApp()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/assets/missing.js", UriKind.Relative));

        // Deny by default: no endpoint matches, so an anonymous caller gets 401, never index.html as JavaScript.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData("/v1/metrics")]
    public async Task UnknownApiPath_IsNotAnsweredWithTheApp(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AppRoute_Always_SendsSecurityHeaders()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/logs", UriKind.Relative));

        var policy = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }
}
