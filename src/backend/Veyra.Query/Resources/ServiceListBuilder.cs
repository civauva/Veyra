using Microsoft.Extensions.Options;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Query.Metrics;
using Veyra.Query.Resources.Api;

namespace Veyra.Query.Resources;

/// <summary>
/// Builds the Services list (ADR 0022): every service whose metrics are kept, its health from the open alerts, and
/// its requests, errors, and latency over the window and the period before, from the SDK's
/// <c>http.server.request.duration</c>.
/// </summary>
/// <remarks>
/// Three reads, at the same time: the inventory of blocks, the request duration series, and the open alerts; then one
/// read of the series' buckets over the window and the period before it together. The buckets are the overview
/// cards' (at most 72 a window), from the rollups beyond six hours (ADR 0015), so the list costs what the overview
/// does.
/// </remarks>
/// <param name="search">The stored metrics.</param>
/// <param name="alerts">The alerts, for health.</param>
/// <param name="options">The Query settings, for when a service is silent.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class ServiceListBuilder(
    IMetricSearch search,
    IAlertStates alerts,
    IOptionsMonitor<QueryOptions> options,
    TimeProvider clock)
{
    /// <summary>How many slices a service's small line of requests has.</summary>
    public const int Slices = 24;

    // One series per route, method, and status code of every instance: room for a large estate.
    private const int _maxDurationSeries = 50_000;

    private static readonly AlertBlockKind[] _kinds = [AlertBlockKind.Service];

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds one page of the list.</summary>
    /// <param name="query">The filters, sort, and page.</param>
    /// <param name="host">Only services that run on this host, from <c>host.name</c>; null for all.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page, or null when the cursor is not one Veyra issued.</returns>
    public async Task<ServiceListResponse?> ListAsync(
        ResourceListQuery query,
        string? host,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var listing = await ReadAsync(query, host, cancellationToken);
        var page = ResourcePage.Of(listing.Items, query.Cursor, query.Limit);

        return page is null ? null : new ServiceListResponse(listing.Header, page.Items, page.Total, page.Next);
    }

    /// <summary>
    /// Builds the whole list before it is cut into pages, with the requests and errors of all of it per bucket, which
    /// the Overview draws as its request activity (ADR 0023).
    /// </summary>
    /// <param name="query">The filters and sort; the cursor and limit are not used.</param>
    /// <param name="host">Only services that run on this host, from <c>host.name</c>; null for all.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The sorted list.</returns>
    public async Task<ServiceListing> ReadAsync(
        ResourceListQuery query,
        string? host,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = clock.GetUtcNow();
        var window = MetricWindow.Card(query.Span, now);
        var both = window.AndBefore();
        var silentSince = now - options.CurrentValue.SilentAfterServices;

        var listing = search.ListBlocksAsync(_kinds, window.End, cancellationToken);
        var finding = search.FindSeriesAsync(
            new MetricSeriesQuery(
                both.Start,
                Names: [MetricNames.RequestDuration],
                Environment: query.Chosen,
                Limit: _maxDurationSeries,
                BlockNameContains: query.Needle),
            cancellationToken);
        var reading = alerts.ListAsync(now, cancellationToken);

        await Task.WhenAll(listing, finding, reading);

        var health = new ResourceHealth(await reading);
        var onHost = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        var known = (await listing)
            .Where(block => query.Holds(block.Environment, everywhereWithout: false) && query.Matches(block.Block.Name))
            .Where(block => onHost is null || block.Hosts.Contains(onHost, StringComparer.OrdinalIgnoreCase))
            .Select(block => (
                Block: block,
                Health: health.Of(AlertBlockKind.Service, block.Block.Name, block.Environment),
                Silent: block.LastSeen < silentSince))
            .Where(item => query.Health.Keeps(item.Health, item.Silent))
            .ToList();

        var keys = known.Select(item => (item.Block.Block.Name, item.Block.Environment)).ToHashSet();
        var durations = (await finding)
            .Where(series => series.ServiceName is { } name && keys.Contains((name, series.Environment)))
            .ToList();
        var data = await _reader.ReadAsync(durations, both, cancellationToken);
        var figures = durations
            .GroupBy(series => (series.ServiceName!, series.Environment))
            .ToDictionary(group => group.Key, group => Figures.Of(data, group.ToList(), window));

        var items = known
            .Select(item =>
            {
                var own = figures.GetValueOrDefault((item.Block.Block.Name, item.Block.Environment));

                return (Item: item, Figures: own, Response: new ServiceItemResponse(
                    item.Block.Block.Name,
                    item.Block.Environment,
                    item.Health,
                    item.Silent,
                    own?.Current.Count is { } count ? count / (window.Starts.Count * window.Bucket.TotalMinutes) : null,
                    own?.Current.Total?.Quantile(0.95),
                    own?.Current.ErrorRatio,
                    item.Block.Instances,
                    item.Block.Hosts,
                    item.Block.LastSeen,
                    ResourceComparison.Slices(window, own?.PerBucket ?? [], Slices)));
            })
            .ToList();

        var sorted = ResourceRanking.Sort(
            items,
            query.Sort,
            item => item.Response.Name,
            item => item.Response.Health,
            item => item.Response.Silent);

        return new ServiceListing(
            window,
            [.. sorted.Select(item => item.Response)],
            Header(items.Select(item => (item.Item.Block, item.Response, item.Figures)).ToList()),
            Sum(items.Select(item => item.Figures?.PerBucket)),
            Sum(items.Select(item => item.Figures?.ErrorsPerBucket)));
    }

    // Adds up what every service counted per bucket; a bucket no service measured stays out.
    private static Dictionary<DateTimeOffset, double> Sum(IEnumerable<Dictionary<DateTimeOffset, double>?> counts) =>
        counts
            .OfType<Dictionary<DateTimeOffset, double>>()
            .SelectMany(perBucket => perBucket)
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value));

    // The header over every service kept: the requests, errors, and latency of all of them together, and how each
    // changed since the period before.
    private static ServiceListHeaderResponse Header(
        IReadOnlyList<(MetricBlockSeen Block, ServiceItemResponse Item, Figures? Figures)> items)
    {
        var current = Period.Sum(items.Select(item => item.Figures?.Current));
        var previous = Period.Sum(items.Select(item => item.Figures?.Previous));

        return new ServiceListHeaderResponse(
            items.Count,
            items.Count(item => item.Item.Health == Home.BlockHealth.Ok),
            items.Count(item => item.Item.Health == Home.BlockHealth.Warning),
            items.Count(item => item.Item.Health == Home.BlockHealth.Caution),
            items.Count(item => item.Item.Silent),
            current.Count,
            ResourceComparison.Rate(current.Count, previous.Count),
            current.ErrorRatio,
            ResourceComparison.Points(current.ErrorRatio, previous.ErrorRatio),
            current.Total?.Quantile(0.95),
            ResourceComparison.Rate(current.Total?.Quantile(0.95), previous.Total?.Quantile(0.95)),
            items.Sum(item => item.Block.Instances),
            items.SelectMany(item => item.Block.Hosts).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // What one service's request durations recorded: in the window, in the period before, and per bucket of the
    // window for the small line and the Overview's request activity, with the 5xx answers among them.
    private sealed record Figures(
        Period Current,
        Period Previous,
        Dictionary<DateTimeOffset, double> PerBucket,
        Dictionary<DateTimeOffset, double> ErrorsPerBucket)
    {
        public static Figures Of(SeriesData data, List<MetricSeries> series, MetricWindow window)
        {
            var all = data.Increases(series);
            var errors = data.Increases(series.Where(ServiceView.IsServerError));

            Period PeriodOf(Func<DateTimeOffset, bool> inside) => new(
                Add(all.Where(pair => inside(pair.Key)).Select(pair => pair.Value)),
                errors.Where(pair => inside(pair.Key)).Sum(pair => pair.Value.Count));

            return new Figures(
                PeriodOf(start => start >= window.Start),
                PeriodOf(start => start < window.Start),
                all.Where(pair => pair.Key >= window.Start)
                    .ToDictionary(pair => pair.Key, pair => (double)pair.Value.Count),
                errors.Where(pair => pair.Key >= window.Start)
                    .ToDictionary(pair => pair.Key, pair => (double)pair.Value.Count));
        }
    }

    // The requests of a period: their distribution, null without any, and how many were answered with a 5xx status.
    private sealed record Period(Distribution? Total, long Errors)
    {
        public long? Count => Total?.Count;

        public double? ErrorRatio => Total is { Count: > 0 } total ? (double)Errors / total.Count : null;

        public static Period Sum(IEnumerable<Period?> periods)
        {
            var kept = periods.OfType<Period>().ToList();

            return new Period(Add(kept.Select(period => period.Total).OfType<Distribution>()), kept.Sum(p => p.Errors));
        }
    }

    // Adds distributions up where their bounds agree with the first, as the overview's figures do.
    private static Distribution? Add(IEnumerable<Distribution> distributions) =>
        distributions.Aggregate((Distribution?)null, (sum, next) => sum is null ? next : sum.Add(next));
}
