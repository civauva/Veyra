using Microsoft.Extensions.Options;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Query.Home;
using Veyra.Query.Metrics;
using Veyra.Query.Metrics.Api;
using Veyra.Query.Resources.Api;

namespace Veyra.Query.Resources;

/// <summary>
/// Builds the Hosts and containers list (ADR 0022): every host and container whose metrics are kept, with what runs on
/// each host, its health from the open alerts, and its CPU, memory, and fullest disk over the window.
/// </summary>
/// <remarks>
/// The figures are worked out as the overview cards work them out (<see cref="HostView"/>,
/// <see cref="ContainerView"/>), from the same series and buckets, with the whole window standing in for the last
/// five minutes: CPU is the window's average, memory and disks their newest value in it. A disk's pace is read from
/// the last day before the window's end, as on the host's screen.
/// </remarks>
/// <param name="search">The stored metrics.</param>
/// <param name="alerts">The alerts, for health.</param>
/// <param name="options">The Query settings, for when a host is silent.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class HostListBuilder(
    IMetricSearch search,
    IAlertStates alerts,
    IOptionsMonitor<QueryOptions> options,
    TimeProvider clock)
{
    /// <summary>The kind of a host in the list.</summary>
    public const string HostKind = "host";

    /// <summary>The kind of a container in the list.</summary>
    public const string ContainerKind = "container";

    /// <summary>The state of an item that is not silent.</summary>
    public const string Online = "online";

    /// <summary>The state of an item that sent nothing for longer than the setting allows.</summary>
    public const string Silent = "silent";

    // How many of the things that run on a host are named; the rest are counted.
    private const int _namedRuns = 3;

    // Several series per CPU and state of every host, and six per container.
    private const int _maxSeries = 50_000;

    private static readonly string[] _hostNames =
        [MetricNames.CpuTime, MetricNames.Memory, MetricNames.Filesystem, MetricNames.Load1];

    private static readonly AlertBlockKind[] _kinds =
        [AlertBlockKind.Service, AlertBlockKind.Host, AlertBlockKind.Database, AlertBlockKind.Container];

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds one page of the list.</summary>
    /// <param name="query">The filters, sort, and page.</param>
    /// <param name="kind"><c>host</c> or <c>container</c>; null for both.</param>
    /// <param name="state"><c>online</c> or <c>silent</c>; null for both.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page, or null when the cursor is not one Veyra issued.</returns>
    public async Task<HostListResponse?> ListAsync(
        ResourceListQuery query,
        string? kind,
        string? state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var listing = await ReadAsync(query, kind, state, cancellationToken);
        var page = ResourcePage.Of(listing.Items, query.Cursor, query.Limit);

        return page is null ? null : new HostListResponse(listing.Header, page.Items, page.Total, page.Next);
    }

    /// <summary>
    /// Builds the whole list before it is cut into pages, with the hosts' CPU, memory, and fullest disk per bucket,
    /// which the Overview draws as its host trends (ADR 0023).
    /// </summary>
    /// <param name="query">The filters and sort; the cursor and limit are not used.</param>
    /// <param name="kind"><c>host</c> or <c>container</c>; null for both.</param>
    /// <param name="state"><c>online</c> or <c>silent</c>; null for both.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The sorted list.</returns>
    public async Task<HostListing> ReadAsync(
        ResourceListQuery query,
        string? kind,
        string? state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = clock.GetUtcNow();
        var window = MetricWindow.Card(query.Span, now);
        var silentSince = now - options.CurrentValue.SilentAfterHosts;
        var hosts = kind is null || string.Equals(kind, HostKind, StringComparison.OrdinalIgnoreCase);
        var containers = kind is null || string.Equals(kind, ContainerKind, StringComparison.OrdinalIgnoreCase);

        var listing = search.ListBlocksAsync(_kinds, window.End, cancellationToken);
        var findingHosts = hosts
            ? search.FindSeriesAsync(Series(query, window, _hostNames), cancellationToken)
            : Task.FromResult<IReadOnlyList<MetricSeries>>([]);
        var findingContainers = containers
            ? search.FindSeriesAsync(Series(query, window, MetricNames.Container), cancellationToken)
            : Task.FromResult<IReadOnlyList<MetricSeries>>([]);
        var reading = alerts.ListAsync(now, cancellationToken);

        await Task.WhenAll(listing, findingHosts, findingContainers, reading);

        var health = new ResourceHealth(await reading);
        var blocks = await listing;
        var hostSeries = (await findingHosts)
            .Where(series => series.Block?.Kind == AlertBlockKind.Host && series.HostName is not null)
            .ToList();
        var containerSeries = (await findingContainers).Where(ContainerView.IsContainer).ToList();

        var readingHosts = _reader.ReadAsync(hostSeries, window, cancellationToken);
        var readingDisks = _reader.ReadAsync(
            hostSeries.Where(series => series.Name == MetricNames.Filesystem).ToList(),
            MetricWindow.Day(window.End),
            cancellationToken);
        var readingContainers = _reader.ReadAsync(containerSeries, window, cancellationToken);

        await Task.WhenAll(readingHosts, readingDisks, readingContainers);

        var hostData = await readingHosts;
        var diskData = await readingDisks;
        var cards = ContainerView.Cards(await readingContainers, await readingContainers)
            .ToDictionary(card => (card.Name, card.Host));

        var items = new List<Item>();

        if (hosts)
        {
            items.AddRange(blocks
                .Where(block => block.Block.Kind == AlertBlockKind.Host)
                .GroupBy(block => block.Block.Name, StringComparer.Ordinal)
                .Where(group => group.Any(block => query.Holds(block.Environment, everywhereWithout: true)))
                .Where(group => query.Matches(group.Key))
                .Select(group =>
                {
                    var newest = group.MaxBy(block => block.LastSeen)!;
                    var own = hostData.Where(series => series.HostName == group.Key);
                    var card = own.Series.Count > 0
                        ? HostView.Card(group.Key, own, own, diskData.Where(series => series.HostName == group.Key))
                        : null;
                    var runs = Runs(blocks, group.Key);

                    return new Item(
                        new HostItemResponse(
                            group.Key,
                            HostKind,
                            card?.Os ?? HostView.OsName(newest.ResourceAttributes),
                            null,
                            health.Of(AlertBlockKind.Host, group.Key, null),
                            newest.LastSeen < silentSince,
                            card?.CpuRatio,
                            card?.MemoryRatio,
                            card?.FullestDisk?.Ratio,
                            [.. runs.Take(_namedRuns)],
                            runs.Count,
                            newest.LastSeen),
                        card?.FillingDisk)
                    {
                        Trend = card is null
                            ? null
                            : new Trend(HostView.CpuBusy(own), HostView.MemoryUsed(own), HostView.FullestDisk(own)),
                    };
                }));
        }

        if (containers)
        {
            items.AddRange(blocks
                .Where(block => block.Block.Kind == AlertBlockKind.Container)
                .GroupBy(block => (block.Block.Name, block.Host))
                .Where(group => group.Any(block => query.Holds(block.Environment, everywhereWithout: true)))
                .Where(group => query.Matches(group.Key.Name))
                .Select(group =>
                {
                    var newest = group.MaxBy(block => block.LastSeen)!;
                    var card = cards.GetValueOrDefault(group.Key);

                    return new Item(
                        new HostItemResponse(
                            group.Key.Name,
                            ContainerKind,
                            null,
                            group.Key.Host,
                            health.Of(AlertBlockKind.Container, group.Key.Name, null, group.Key.Host),
                            newest.LastSeen < silentSince,
                            card?.CpuPercent / 100,
                            card?.MemoryRatio,
                            null,
                            [],
                            0,
                            newest.LastSeen),
                        null);
                }));
        }

        var kept = items
            .Where(item => state is null
                || string.Equals(state, Silent, StringComparison.OrdinalIgnoreCase) == item.Response.Silent)
            .Where(item => query.Health.Keeps(item.Response.Health, item.Response.Silent))
            .ToList();
        var sorted = ResourceRanking.Sort(
            kept,
            query.Sort,
            item => item.Response.Name,
            item => item.Response.Health,
            item => item.Response.Silent);
        var trends = kept.Select(item => item.Trend).OfType<Trend>().ToList();

        return new HostListing(
            window,
            [.. sorted.Select(item => item.Response)],
            Header(kept),
            Across(trends.Select(trend => trend.Cpu), values => values.Average()),
            Across(trends.Select(trend => trend.Memory), values => values.Average()),
            Across(trends.Select(trend => trend.Disk), values => values.Max()));
    }

    // One value per bucket across hosts: the hosts that measured the bucket, put together.
    private static Dictionary<DateTimeOffset, double> Across(
        IEnumerable<Dictionary<DateTimeOffset, double>> perHost,
        Func<IEnumerable<double>, double> together) =>
        perHost
            .SelectMany(values => values)
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => together(group.Select(pair => pair.Value)));

    private static MetricSeriesQuery Series(
        ResourceListQuery query,
        MetricWindow window,
        IReadOnlyList<string> names) =>
        new(
            window.Start,
            Names: names,
            Environment: query.Chosen,
            Limit: _maxSeries,
            BlockNameContains: query.Needle,
            OrNoEnvironment: true);

    // What runs on a host: the services, database servers, and containers whose series name it, alphabetically.
    private static List<string> Runs(IReadOnlyList<MetricBlockSeen> blocks, string host) =>
    [
        .. blocks
            .Where(block => block.Block.Kind != AlertBlockKind.Host)
            .Where(block => block.Block.Kind == AlertBlockKind.Container
                ? string.Equals(block.Host, host, StringComparison.OrdinalIgnoreCase)
                : block.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            .Select(block => block.Block.Server ?? block.Block.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.OrdinalIgnoreCase),
    ];

    private static HostListHeaderResponse Header(IReadOnlyList<Item> items)
    {
        var hosts = items.Where(item => item.Response.Kind == HostKind).ToList();
        var cpu = items.Where(item => item.Response.CpuRatio is not null).ToList();
        var memory = items.Where(item => item.Response.MemoryRatio is not null).ToList();
        var filling = hosts
            .Where(item => item.Filling?.DaysUntilFull is not null)
            .OrderBy(item => item.Filling!.DaysUntilFull)
            .ThenBy(item => item.Response.Name, StringComparer.Ordinal)
            .ToList();
        var busiest = cpu.MaxBy(item => item.Response.CpuRatio);
        var fullest = memory.MaxBy(item => item.Response.MemoryRatio);
        var first = filling.FirstOrDefault();

        return new HostListHeaderResponse(
            hosts.Count,
            hosts.Count(item => !item.Response.Silent),
            items.Count(item => item.Response.Kind == ContainerKind),
            cpu.Count == 0 ? null : cpu.Average(item => item.Response.CpuRatio!.Value),
            busiest is null ? null : new NamedRatioResponse(busiest.Response.Name, busiest.Response.CpuRatio!.Value),
            memory.Count == 0 ? null : memory.Average(item => item.Response.MemoryRatio!.Value),
            fullest is null ? null : new NamedRatioResponse(fullest.Response.Name, fullest.Response.MemoryRatio!.Value),
            filling.Count,
            first is null
                ? null
                : new DiskFillingResponse(
                    first.Response.Name,
                    first.Filling!.Mountpoint,
                    Math.Round(first.Filling.DaysUntilFull!.Value * 24)));
    }

    // An item of the list, with the disk that fills soonest, which the header needs and the item does not tell, and for
    // a host that reports its figures, their trend for the Overview.
    private sealed record Item(HostItemResponse Response, DiskResponse? Filling)
    {
        public Trend? Trend { get; init; }
    }

    // A host's CPU, memory, and fullest disk in use per bucket start, each from 0 to 1.
    private sealed record Trend(
        Dictionary<DateTimeOffset, double> Cpu,
        Dictionary<DateTimeOffset, double> Memory,
        Dictionary<DateTimeOffset, double> Disk);
}
