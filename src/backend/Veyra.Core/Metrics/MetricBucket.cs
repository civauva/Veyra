namespace Veyra.Core.Metrics;

/// <summary>
/// The points of one series in one time bucket, summarised so that rates and percentiles can be computed from it
/// (ADR 0010): the average and total of the values, and the last point as it was stored.
/// </summary>
/// <param name="SeriesId">The series.</param>
/// <param name="Start">The start of the bucket.</param>
/// <param name="Points">How many points fell into it.</param>
/// <param name="Average">The average value; null for histograms.</param>
/// <param name="Total">The sum of the values, for delta sums; null for histograms.</param>
/// <param name="LastTimestamp">When the last point of the bucket was measured.</param>
/// <param name="LastStart">The start time of that point, if sent.</param>
/// <param name="LastValue">Its value; null for histograms.</param>
/// <param name="LastHistogram">Its distribution; null for gauges and sums.</param>
public sealed record MetricBucket(
    long SeriesId,
    DateTimeOffset Start,
    int Points,
    double? Average,
    double? Total,
    DateTimeOffset LastTimestamp,
    DateTimeOffset? LastStart,
    double? LastValue,
    HistogramValue? LastHistogram);
