using Veyra.Core.Metrics;

namespace Veyra.Storage.Metrics;

/// <summary>
/// Stands in for <see cref="MetricStore"/> when no connection string is configured, so Veyra can start and report the
/// problem through <c>/readyz</c> instead of failing at startup.
/// </summary>
internal sealed class UnconfiguredMetricStore : IMetricWriter, IMetricSearch
{
    /// <summary>Always fails: there is no database to write to.</summary>
    /// <param name="points">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<MetricWriteResult> WriteAsync(IReadOnlyList<MetricPoint> points, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <summary>Always fails: there is no database to search.</summary>
    /// <param name="query">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<IReadOnlyList<MetricSeries>> FindSeriesAsync(
        MetricSeriesQuery query,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <summary>Always fails: there is no database to read.</summary>
    /// <param name="seriesIds">Ignored.</param>
    /// <param name="windowStart">Ignored.</param>
    /// <param name="windowEnd">Ignored.</param>
    /// <param name="bucketSize">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<IReadOnlyList<MetricBucket>> ReadBucketsAsync(
        IReadOnlyList<long> seriesIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeSpan bucketSize,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <summary>Always fails: there is no database to read.</summary>
    /// <param name="search">Ignored.</param>
    /// <param name="seenSince">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<IReadOnlyList<MetricName>> ListNamesAsync(
        string? search,
        DateTimeOffset seenSince,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <summary>Always fails: there is no database to measure.</summary>
    /// <param name="now">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<MetricStorageUsage> GetUsageAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);
}
