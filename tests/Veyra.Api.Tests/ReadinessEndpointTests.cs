using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Veyra.Api.Tests;

public class ReadinessEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReadinessEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Readyz_StorageNotConfigured_ReturnsServiceUnavailable()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/readyz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
