namespace Veyra.Core.Metrics;

/// <summary>
/// Reads stored metrics. The Storage module implements it; the Query module computes rates and percentiles from what
/// it returns (ADR 0010).
/// </summary>
public interface IMetricSearch
{
    /// <summary>Finds series by name, service, environment, and host.</summary>
    /// <param name="query">The filters.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The series, ordered by name and then by ID.</returns>
    Task<IReadOnlyList<MetricSeries>> FindSeriesAsync(MetricSeriesQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Summarises the points of some series per time bucket. Buckets without points are left out.
    /// </summary>
    /// <param name="seriesIds">The series.</param>
    /// <param name="windowStart">The start of the window, inclusive; buckets are aligned to it.</param>
    /// <param name="windowEnd">The end of the window, exclusive.</param>
    /// <param name="bucketSize">The width of each bucket.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The buckets, by series and then by start.</returns>
    Task<IReadOnlyList<MetricBucket>> ReadBucketsAsync(
        IReadOnlyList<long> seriesIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeSpan bucketSize,
        CancellationToken cancellationToken);

    /// <summary>Lists the metric names seen since a time.</summary>
    /// <param name="search">Only names that contain this text, ignoring case; null for all.</param>
    /// <param name="seenSince">Only names with a point at or after this time.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The names, alphabetically.</returns>
    Task<IReadOnlyList<MetricName>> ListNamesAsync(
        string? search,
        DateTimeOffset seenSince,
        CancellationToken cancellationToken);

    /// <summary>Measures how much metric data is stored.</summary>
    /// <param name="now">The current time, for the point rate.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The usage.</returns>
    Task<MetricStorageUsage> GetUsageAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
