using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Veyra.Api.Tests.Access;

/// <summary>
/// Guards ADR 0008's "deny by default": only a known, short list of endpoints may be reached without signing in.
/// A new anonymous endpoint fails this test until it is added here on purpose.
/// </summary>
public class AnonymousEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] _allowed =
    [
        "GET /healthz",
        // Health checks answer any method; they reveal nothing beyond healthy or not.
        "ANY /readyz",
        "GET /api/v1/setup",
        "POST /api/v1/setup",
        "POST /api/v1/session",
        // OTLP checks ingestion keys itself, not sessions.
        "POST /v1/logs",
        "POST /opentelemetry.proto.collector.logs.v1.LogsService/Export",
        // The single-page app: index.html for its own routes, never for the API or OTLP.
        "GET /",
        "HEAD /",
        "GET /{*path:nonfile:regex(^(?!api/|v1/).*$)}",
        "HEAD /{*path:nonfile:regex(^(?!api/|v1/).*$)}",
    ];

    [Fact]
    public void Endpoints_AllowingAnonymousAccess_AreExactlyTheAllowedOnes()
    {
        var anonymous = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(endpoint => Methods(endpoint)
                .Select(method => $"{method} /{endpoint.RoutePattern.RawText!.TrimStart('/')}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(_allowed.Order(StringComparer.Ordinal), anonymous);
    }

    private static IEnumerable<string> Methods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"];
}
