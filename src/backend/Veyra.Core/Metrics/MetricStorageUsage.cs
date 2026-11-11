namespace Veyra.Core.Metrics;

/// <summary>How much metric data Veyra holds.</summary>
/// <param name="Series">Stored series.</param>
/// <param name="MaxSeries">The series limit.</param>
/// <param name="PointsPerSecond">Points received over the last five minutes, per second.</param>
/// <param name="UsedBytes">Disk space of series and points, indexes included.</param>
/// <param name="RetentionDays">How many days of points are kept.</param>
public sealed record MetricStorageUsage(
    int Series,
    int MaxSeries,
    double PointsPerSecond,
    long UsedBytes,
    int RetentionDays);
