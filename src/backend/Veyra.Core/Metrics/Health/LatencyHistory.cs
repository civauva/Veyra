namespace Veyra.Core.Metrics.Health;

/// <summary>A service's request durations over a period, for an alert's chart and its slowest routes.</summary>
/// <param name="Bucket">The width of each point.</param>
/// <param name="Points">One point per bucket, oldest first.</param>
/// <param name="Routes">Its routes over the whole period, slowest first.</param>
public sealed record LatencyHistory(
    TimeSpan Bucket,
    IReadOnlyList<LatencyPoint> Points,
    IReadOnlyList<RouteLatency> Routes);
