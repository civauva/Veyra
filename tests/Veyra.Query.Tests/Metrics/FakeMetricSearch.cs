using Veyra.Core.Alerts;
using Veyra.Core.Metrics;

namespace Veyra.Query.Tests.Metrics;

/// <summary>
/// Holds series and buckets a test adds and answers queries the way the real store does: by name, service,
/// environment, host, and last-seen time, and buckets inside the window.
/// </summary>
internal sealed class FakeMetricSearch : IMetricSearch
{
    private long _nextId = 1;

    public List<MetricSeries> Series { get; } = [];

    public List<MetricBucket> Buckets { get; } = [];

    /// <summary>Adds a series and returns it with its ID.</summary>
    public MetricSeries Add(MetricSeries series)
    {
        var stored = series with { Id = _nextId++ };
        Series.Add(stored);

        return stored;
    }

    /// <summary>Adds one bucket of a series holding a single point; the series counts from its first-seen time.</summary>
    public void Point(MetricSeries series, DateTimeOffset bucket, double? value, HistogramValue? histogram = null) =>
        Buckets.Add(new MetricBucket(
            series.Id,
            bucket,
            1,
            value,
            value,
            bucket.AddSeconds(30),
            series.FirstSeen,
            value,
            histogram));

    public Task<IReadOnlyList<MetricSeries>> FindSeriesAsync(
        MetricSeriesQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricSeries>>(Series
            .Where(series => series.LastSeen >= query.SeenSince)
            .Where(series => query.Names is null || query.Names.Contains(series.Name))
            .Where(series => query.ServiceName is null || series.ServiceName == query.ServiceName)
            .Where(series => query.Environment is null
                || series.Environment == query.Environment
                || (query.OrNoEnvironment && series.Environment is null))
            .Where(series => query.HostName is null || series.HostName == query.HostName)
            .Where(series => query.Block is null || series.Block == query.Block)
            .Where(series => string.IsNullOrWhiteSpace(query.BlockNameContains)
                || series.Block?.Name.Contains(query.BlockNameContains.Trim(), StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(series => series.Name, StringComparer.Ordinal)
            .ThenBy(series => series.Id)
            .Take(query.Limit)
            .ToList());

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
            .OrderBy(bucket => bucket.SeriesId)
            .ThenBy(bucket => bucket.Start)
            .ToList());

    public Task<IReadOnlyList<MetricName>> ListNamesAsync(
        string? search,
        DateTimeOffset seenSince,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricName>>(Series
            .Where(series => series.LastSeen >= seenSince)
            .Where(series => search is null || series.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .GroupBy(series => series.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new MetricName(
                group.Key,
                group.First().Kind,
                group.First().Unit,
                group.First().Description,
                group.Count(),
                group.Select(series => series.ServiceName ?? series.HostName).OfType<string>().Distinct().ToList()))
            .ToList());

    /// <summary>Groups the series by block as the real store does: per environment, and per container host.</summary>
    public Task<IReadOnlyList<MetricBlockSeen>> ListBlocksAsync(
        IReadOnlyList<AlertBlockKind> kinds,
        DateTimeOffset firstSeenBefore,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricBlockSeen>>(Series
            .Where(series => series.Block is not null && kinds.Contains(series.Block.Kind))
            .Where(series => series.FirstSeen < firstSeenBefore)
            .GroupBy(series => (
                series.Block,
                series.Environment,
                Host: series.Block!.Kind == AlertBlockKind.Container ? series.HostName : null))
            .OrderBy(group => group.Key.Block!.Kind)
            .ThenBy(group => group.Key.Block!.Name, StringComparer.Ordinal)
            .Select(group => new MetricBlockSeen(
                group.Key.Block!,
                group.Key.Environment,
                group.Key.Host,
                [.. group.Select(series => series.HostName).OfType<string>().Distinct().Order(StringComparer.Ordinal)],
                group.Select(series => series.ResourceAttributes.GetValueOrDefault("service.instance.id")
                        ?? series.HostName)
                    .OfType<string>()
                    .Distinct()
                    .Count(),
                group.MaxBy(series => series.LastSeen)!.ResourceAttributes,
                group.Min(series => series.FirstSeen),
                group.Max(series => series.LastSeen)))
            .ToList());

    public Task<MetricStorageUsage> GetUsageAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new MetricStorageUsage(Series.Count, 50_000, 2.5, 4_096, 30));
}
