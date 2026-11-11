namespace Veyra.Core.Metrics.Health;

/// <summary>The share of a disk in use in one bucket of time.</summary>
/// <param name="At">The start of the bucket.</param>
/// <param name="Ratio">The share in use, from 0 to 1; <see langword="null"/> when nothing was measured.</param>
public sealed record DiskUsagePoint(DateTimeOffset At, double? Ratio);
