namespace Veyra.Query.Metrics.Api;

/// <summary>One HTTP route of a service over the last five minutes.</summary>
/// <param name="Method">The HTTP method, if the SDK records it.</param>
/// <param name="Route">The route template, for example <c>/payments/{id}</c>; <c>(none)</c> without one.</param>
/// <param name="RequestsPerSecond">Requests per second.</param>
/// <param name="P95Seconds">The 95th percentile of their duration.</param>
/// <param name="ServerErrorRatio">The share answered with a 5xx status.</param>
public sealed record RouteResponse(
    string? Method,
    string Route,
    double RequestsPerSecond,
    double? P95Seconds,
    double? ServerErrorRatio);
