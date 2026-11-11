namespace Veyra.Core.Metrics.Health;

/// <summary>The share of one disk in use over a period, for an alert's chart.</summary>
/// <param name="Bucket">The width of each point.</param>
/// <param name="Points">One point per bucket, oldest first.</param>
public sealed record DiskHistory(TimeSpan Bucket, IReadOnlyList<DiskUsagePoint> Points);
