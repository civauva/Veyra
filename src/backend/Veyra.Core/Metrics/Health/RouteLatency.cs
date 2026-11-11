namespace Veyra.Core.Metrics.Health;

/// <summary>How fast one route of a service answered over a period.</summary>
/// <param name="Method">The HTTP method, when the SDK sends it.</param>
/// <param name="Route">The route template, for example <c>/orders/{id}</c>, or <c>(none)</c>.</param>
/// <param name="RequestsPerSecond">The requests per second over the period.</param>
/// <param name="P95Seconds">The 95th percentile of its durations; <see langword="null"/> without requests.</param>
/// <param name="ServerErrorRatio">
/// The share of its requests that answered 5xx; <see langword="null"/> without requests.
/// </param>
public sealed record RouteLatency(
    string? Method,
    string Route,
    double RequestsPerSecond,
    double? P95Seconds,
    double? ServerErrorRatio);
