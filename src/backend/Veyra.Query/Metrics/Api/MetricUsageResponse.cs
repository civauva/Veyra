namespace Veyra.Query.Metrics.Api;

/// <summary>How much metric data Veyra holds.</summary>
/// <param name="Series">Stored series.</param>
/// <param name="MaxSeries">The series limit.</param>
/// <param name="PointsPerSecond">Points received per second over the last five minutes.</param>
/// <param name="UsedBytes">Disk space of series and points.</param>
/// <param name="RetentionDays">How many days of points are kept.</param>
/// <param name="Names">How many metric names were seen within the window.</param>
public sealed record MetricUsageResponse(
    int Series,
    int MaxSeries,
    double PointsPerSecond,
    long UsedBytes,
    int RetentionDays,
    int Names);
