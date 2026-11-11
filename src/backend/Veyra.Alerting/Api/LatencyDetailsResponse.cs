using Veyra.Core.Metrics.Health;

namespace Veyra.Alerting.Api;

/// <summary>
/// The request durations of a latency alert's service: from 30 minutes before it opened to 10 minutes after it
/// resolved, or to now, with its slowest routes while it was open.
/// </summary>
/// <param name="BucketSeconds">The width of each point: one minute, wider for long alerts.</param>
/// <param name="From">The start of the first point.</param>
/// <param name="To">The end of the last point.</param>
/// <param name="Points">The median, the 95th percentile, and the requests per point, oldest first.</param>
/// <param name="Routes">The service's routes while the alert was open, slowest first.</param>
public sealed record LatencyDetailsResponse(
    int BucketSeconds,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<LatencyPoint> Points,
    IReadOnlyList<RouteLatency> Routes);
