namespace Veyra.Core.Metrics.Health;

/// <summary>A service's request durations in one bucket of time.</summary>
/// <param name="At">The start of the bucket.</param>
/// <param name="P50Seconds">The median; <see langword="null"/> without requests.</param>
/// <param name="P95Seconds">The 95th percentile; <see langword="null"/> without requests.</param>
/// <param name="Requests">How many requests it answered in the bucket.</param>
public sealed record LatencyPoint(DateTimeOffset At, double? P50Seconds, double? P95Seconds, long Requests);
