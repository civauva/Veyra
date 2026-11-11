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
    IReadOnlyList<ChartLineResponse> Runtime);
