using Veyra.Core.Metrics;

namespace Veyra.Query.Metrics;

/// <summary>Reads series and their buckets for a window, in two calls to the Storage module.</summary>
/// <param name="search">The stored metrics.</param>
internal sealed class MetricReader(IMetricSearch search)
{
    /// <summary>Finds series and reads their buckets over a window, its baseline bucket included.</summary>
    /// <param name="query">Which series.</param>
    /// <param name="window">The window.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The series and their buckets.</returns>
    public async Task<SeriesData> ReadAsync(
        MetricSeriesQuery query,
        MetricWindow window,
        CancellationToken cancellationToken)
    {
        var series = await search.FindSeriesAsync(query, cancellationToken);

        return await ReadAsync(series, window, cancellationToken);
    }

    /// <summary>Reads the buckets of known series over a window, its baseline bucket included.</summary>
    /// <param name="series">The series.</param>
    /// <param name="window">The window.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The series and their buckets.</returns>
    public async Task<SeriesData> ReadAsync(
        IReadOnlyList<MetricSeries> series,
        MetricWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (series.Count == 0)
        {
            return SeriesData.Empty(window);
        }

        var buckets = await search.ReadBucketsAsync(
            series.Select(one => one.Id).ToList(),
            window.ReadStart,
            window.End,
            window.Bucket,
            cancellationToken);

        var bySeries = buckets
            .GroupBy(bucket => bucket.SeriesId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MetricBucket>)group.OrderBy(bucket => bucket.Start).ToList());

        return new SeriesData(window, series, bySeries);
    }
}
