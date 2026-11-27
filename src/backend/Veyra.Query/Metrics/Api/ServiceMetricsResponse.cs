namespace Veyra.Query.Metrics.Api;

/// <summary>One service's metrics over a window.</summary>
/// <param name="Card">The numbers of its overview card.</param>
/// <param name="Range">The window, for example <c>6h</c>.</param>
/// <param name="BucketSeconds">The width of each point.</param>
/// <param name="InstanceNames">Its instances: <c>service.instance.id</c>, or the host when there is none.</param>
/// <param name="Latency">Request duration in seconds: <c>p50</c>, <c>p95</c>, and <c>p99</c>.</param>
/// <param name="Requests">Requests per second: <c>ok</c> (2xx and 3xx), <c>4xx</c>, and <c>5xx</c>.</param>
/// <param name="Routes">The busiest routes over the last five minutes, at most ten.</param>
/// <param name="Resources">CPU in use (<c>cpu</c>, from 0 to 1) and memory (<c>memory</c>, bytes).</param>
/// <param name="Runtime">
/// Runtime metrics where the SDK sends them: <c>gc</c> collections per minute, <c>queue</c> thread pool queue length,
/// and <c>threads</c>.
/// </param>
public sealed record ServiceMetricsResponse(
    ServiceCardResponse Card,
    string Range,
    int BucketSeconds,
    IReadOnlyList<string> InstanceNames,
    IReadOnlyList<ChartLineResponse> Latency,
    IReadOnlyList<ChartLineResponse> Requests,
    IReadOnlyList<RouteResponse> Routes,
    IReadOnlyList<ChartLineResponse> Resources,
    IReadOnlyList<ChartLineResponse> Runtime)
{
    /// <summary>The start of the period the answer covers (ADR 0022).</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>The end of the period the answer covers: now, or the <c>to</c> asked for (ADR 0022).</summary>
    public DateTimeOffset To { get; init; }

    /// <summary>
    /// Up to five routes, the slowest first by their 95th percentile, from every route and not only the busiest, for
    /// the Simple view (VEY-68).
    /// </summary>
    public IReadOnlyList<RouteResponse> SlowestRoutes { get; init; } = [];
}
