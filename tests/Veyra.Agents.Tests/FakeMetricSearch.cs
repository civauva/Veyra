using Veyra.Core.Alerts;
using Veyra.Core.Metrics;

namespace Veyra.Agents.Tests;

/// <summary>Answers metric reads with the series and buckets a test gives it.</summary>
internal sealed class FakeMetricSearch : IMetricSearch
{
    /// <summary>The series every search answers with, filtered by name when the query names some.</summary>
    public List<MetricSeries> Series { get; } = [];

    /// <summary>The buckets every read answers with, filtered by series.</summary>
    public List<MetricBucket> Buckets { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<MetricSeries>> FindSeriesAsync(
        MetricSeriesQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricSeries>>(Series
            .Where(series => query.Names is null || query.Names.Contains(series.Name))
            .Take(query.Limit)
            .ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<MetricBucket>> ReadBucketsAsync(
        IReadOnlyList<long> seriesIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeSpan bucketSize,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricBucket>>(Buckets
            .Where(bucket => seriesIds.Contains(bucket.SeriesId)
                && bucket.Start >= windowStart
                && bucket.Start < windowEnd)
            .ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<MetricBlockSeen>> ListBlocksAsync(
        IReadOnlyList<AlertBlockKind> kinds,
        DateTimeOffset firstSeenBefore,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IReadOnlyList<MetricName>> ListNamesAsync(
        string? search,
        DateTimeOffset seenSince,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<MetricStorageUsage> GetUsageAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
