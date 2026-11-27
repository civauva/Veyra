using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Puts together the Metrics screens: the overview of hosts, services, database servers, and containers, one host,
/// one service, and one metric for the browser. Each view reads its series once for the chosen window and once for
/// the last five minutes.
/// </summary>
/// <param name="search">The stored metrics.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class MetricViewBuilder(IMetricSearch search, TimeProvider clock)
{
    /// <summary>The most lines a browser chart shows.</summary>
    public const int MaxLines = 8;

    // A host or service that sent nothing for this long is left off the overview.
    private static readonly TimeSpan _activeWithin = TimeSpan.FromMinutes(15);

    private static readonly string[] _hostCard =
        [MetricNames.CpuTime, MetricNames.Memory, MetricNames.Filesystem, MetricNames.Load1];

    private static readonly string[] _serviceCard =
    [
        MetricNames.RequestDuration,
        MetricNames.DotnetCpuTime,
        MetricNames.ProcessCpuTime,
        MetricNames.DotnetCpuCount,
        MetricNames.DotnetWorkingSet,
        MetricNames.ProcessMemory,
    ];

    // What a container's card and row read over the window: its CPU line and its restarts.
    private static readonly string[] _containerCard = [MetricNames.ContainerCpu, MetricNames.ContainerRestarts];

    // Six series per container: room for well over a thousand containers.
    private const int _maxContainerSeries = 10_000;

    // What a database server's card reads, SQL Server's and PostgreSQL's alike (ADR 0018).
    private static readonly string[] _databaseCard = [.. SqlServerNames.Card, .. MetricNames.PostgresScreen];

    // What a database server's card line reads over the window: batch requests, or transactions.
    private static readonly string[] _databaseLines =
        [SqlServerNames.BatchRequests, MetricNames.PostgresCommits, MetricNames.PostgresRollbacks];

    // What a database server's deadlocks are counted from over the last 15 minutes.
    private static readonly string[] _databaseDeadlocks = [SqlServerNames.DeadlockRate, MetricNames.PostgresDeadlocks];

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds the overview of every card.</summary>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The overview.</returns>
    public Task<MetricsOverviewResponse> OverviewAsync(MetricSpan span, CancellationToken cancellationToken) =>
        OverviewAsync(span, MetricsOverviewFilter.None, cancellationToken);

    /// <summary>Builds the overview of the cards a search and an environment keep.</summary>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="filter">
    /// The search and the environment. They narrow the series reads before their limits, and then each card exactly.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The overview.</returns>
    public async Task<MetricsOverviewResponse> OverviewAsync(
        MetricSpan span,
        MetricsOverviewFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var window = MetricWindow.Card(span, clock.GetUtcNow());
        var now = window.End;
        var recent = MetricWindow.Recent(now);

        // The reads do not depend on each other, so they run at the same time: the overview takes as long as its
        // slowest read, not all of them together (ADR 0015).
        var findingHosts = search.FindSeriesAsync(
            filter.Narrow(new MetricSeriesQuery(now - _activeWithin, Names: _hostCard), everywhereWithout: true),
            cancellationToken);
        var findingServices = search.FindSeriesAsync(
            filter.Narrow(new MetricSeriesQuery(now - _activeWithin, Names: _serviceCard), everywhereWithout: false),
            cancellationToken);
        var findingDatabases = search.FindSeriesAsync(
            filter.Narrow(new MetricSeriesQuery(now - _activeWithin, Names: _databaseCard), everywhereWithout: true),
            cancellationToken);
        var findingContainers = search.FindSeriesAsync(
            filter.Narrow(
                new MetricSeriesQuery(now - _activeWithin, Names: MetricNames.Container, Limit: _maxContainerSeries),
                everywhereWithout: true),
            cancellationToken);
        var measuringUsage = search.GetUsageAsync(clock.GetUtcNow(), cancellationToken);
        var listingNames = search.ListNamesAsync(null, window.Start, cancellationToken);

        var hostSeries = (await findingHosts)
            .Where(series => series.HostName is not null && series.ServiceName is null)
            .ToList();
        var serviceSeries = (await findingServices)
            .Where(series => series.ServiceName is not null)
            .ToList();
        var databaseSeries = (await findingDatabases)
            .Where(series => series.Block?.Kind == AlertBlockKind.Database)
            .ToList();
        var containerSeries = (await findingContainers).Where(ContainerView.IsContainer).ToList();

        var readingHostWindow = _reader.ReadAsync(
            hostSeries.Where(series => series.Name == MetricNames.CpuTime).ToList(),
            window,
            cancellationToken);
        var readingHostRecent = _reader.ReadAsync(hostSeries, recent, cancellationToken);
        var readingHostDay = _reader.ReadAsync(
            hostSeries.Where(series => series.Name == MetricNames.Filesystem).ToList(),
            MetricWindow.Day(now),
            cancellationToken);
        var readingServiceWindow = _reader.ReadAsync(
            serviceSeries.Where(series => series.Name == MetricNames.RequestDuration).ToList(),
            window,
            cancellationToken);
        var readingServiceRecent = _reader.ReadAsync(serviceSeries, recent, cancellationToken);
        var readingDatabaseWindow = _reader.ReadAsync(
            databaseSeries.Where(series => _databaseLines.Contains(series.Name)).ToList(),
            window,
            cancellationToken);
        var readingDatabaseRecent = _reader.ReadAsync(databaseSeries, recent, cancellationToken);
        var readingDatabaseQuarter = _reader.ReadAsync(
            databaseSeries.Where(series => _databaseDeadlocks.Contains(series.Name)).ToList(),
            DatabaseViewBuilder.Quarter(now),
            cancellationToken);
        var readingContainerWindow = _reader.ReadAsync(
            containerSeries.Where(series => _containerCard.Contains(series.Name)).ToList(),
            window,
            cancellationToken);
        var readingContainerRecent = _reader.ReadAsync(containerSeries, recent, cancellationToken);

        await Task.WhenAll(
            readingHostWindow,
            readingHostRecent,
            readingHostDay,
            readingServiceWindow,
            readingServiceRecent,
            readingDatabaseWindow,
            readingDatabaseRecent,
            readingDatabaseQuarter,
            readingContainerWindow,
            readingContainerRecent,
            measuringUsage,
            listingNames);

        var hostWindow = await readingHostWindow;
        var hostRecent = await readingHostRecent;
        var hostDay = await readingHostDay;
        var serviceWindow = await readingServiceWindow;
        var serviceRecent = await readingServiceRecent;
        var databaseWindow = await readingDatabaseWindow;
        var databaseRecent = await readingDatabaseRecent;
        var databaseQuarter = await readingDatabaseQuarter;

        var hosts = hostSeries
            .Select(series => series.HostName!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(host => HostView.Card(
                host,
                hostWindow.Where(series => series.HostName == host),
                hostRecent.Where(series => series.HostName == host),
                hostDay.Where(series => series.HostName == host)))
            .ToList();

        var services = serviceSeries
            .Select(series => (Service: series.ServiceName!, series.Environment))
            .Distinct()
            .OrderBy(key => key.Service, StringComparer.Ordinal)
            .ThenBy(key => key.Environment, StringComparer.Ordinal)
            .Select(key => ServiceView.Card(
                key.Service,
                key.Environment,
                serviceWindow.Where(series => Belongs(series, key.Service, key.Environment)),
                serviceRecent.Where(series => Belongs(series, key.Service, key.Environment))))
            .ToList();

        // By the server's name, whatever its engine, as the overview and the switcher list them (VEY-68).
        var databases = databaseSeries
            .Select(series => series.Block!)
            .Distinct()
            .OrderBy(block => block.Server, StringComparer.OrdinalIgnoreCase)
            .ThenBy(block => block.Name, StringComparer.Ordinal)
            .Select(block => block.Engine == MetricNames.PostgresEngine
                ? PostgresView.Card(
                    block,
                    databaseWindow.Where(series => series.Block == block),
                    databaseRecent.Where(series => series.Block == block),
                    databaseQuarter.Where(series => series.Block == block))
                : DatabaseView.Card(
                    block,
                    databaseWindow.Where(series => series.Block == block),
                    databaseRecent.Where(series => series.Block == block),
                    databaseQuarter.Where(series => series.Block == block)))
            .ToList();

        var containers = ContainerView.Cards(await readingContainerWindow, await readingContainerRecent);

        // The series reads were narrowed before their limits; each card is now held to the filter exactly, since a
        // card's name and environment can differ from the block name of a single series.
        hosts = [.. hosts.Where(card => filter.Matches(card.Name) && filter.Holds(card.Environment, true))];
        services = [.. services.Where(card => filter.Matches(card.Name) && filter.Holds(card.Environment, false))];
        databases = [.. databases.Where(card => filter.Matches(card.Server) && filter.Holds(card.Environment, true))];
        containers = [.. containers.Where(card => filter.Matches(card.Name) && filter.Holds(card.Environment, true))];

        var usage = await measuringUsage;
        var names = await listingNames;
        var listed = hosts.Count + services.Count + databases.Count + containers.Count;

        return new MetricsOverviewResponse(
            now,
            window.Range,
            (int)window.Bucket.TotalSeconds,
            new MetricUsageResponse(
                usage.Series,
                usage.MaxSeries,
                usage.PointsPerSecond,
                usage.UsedBytes,
                usage.RetentionDays,
                names.Count),
            new MetricsSummaryResponse(
                hosts.Count,
                hosts.Count(host => host.FillingDisk is not null),
                services.Count,
                databases.Count,
                containers.Count,
                containers.Count(container => container.Restarts > 0),
                listed > 0 || (filter.Filters && await AnySentAsync(now, cancellationToken))),
            hosts,
            services,
            databases,
            containers)
        {
            From = window.From,
            To = window.End,
        };
    }

    // Whether any card's series sent recently, when the filter left no card: one row is enough to tell.
    private async Task<bool> AnySentAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        (await search.FindSeriesAsync(
            new MetricSeriesQuery(
                now - _activeWithin,
                Names: [.. _hostCard, .. _serviceCard, .. _databaseCard, .. MetricNames.Container],
                Limit: 1),
            cancellationToken)).Count > 0;

    /// <summary>Builds one host's view.</summary>
    /// <param name="host">The host name.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The view, or null when the host sent no host metrics within the window.</returns>
    public async Task<HostMetricsResponse?> HostAsync(string host, MetricSpan span, CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(span, clock.GetUtcNow());
        var now = window.End;
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(window.Start, Names: MetricNames.Host, HostName: host),
                cancellationToken))
            .Where(one => one.ServiceName is null)
            .ToList();

        if (series.Count == 0)
        {
            return null;
        }

        var containers = (await search.FindSeriesAsync(
                new MetricSeriesQuery(
                    window.Start,
                    Names: MetricNames.Container,
                    HostName: host,
                    Limit: _maxContainerSeries),
                cancellationToken))
            .Where(ContainerView.IsContainer)
            .ToList();

        var view = HostView.Detail(
            host,
            await _reader.ReadAsync(series, window, cancellationToken),
            await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken),
            await _reader.ReadAsync(
                series.Where(one => one.Name == MetricNames.Filesystem).ToList(),
                MetricWindow.Day(now),
                cancellationToken));

        if (containers.Count == 0)
        {
            return view;
        }

        return view with
        {
            Containers = ContainerView.Cards(
                await _reader.ReadAsync(
                    containers.Where(one => _containerCard.Contains(one.Name)).ToList(),
                    window,
                    cancellationToken),
                await _reader.ReadAsync(containers, MetricWindow.Recent(now), cancellationToken)),
        };
    }

    /// <summary>Builds one service's view.</summary>
    /// <param name="service">The service name.</param>
    /// <param name="environment">The environment; null for a service that names none.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The view, or null when the service sent none of these metrics within the window.</returns>
    public async Task<ServiceMetricsResponse?> ServiceAsync(
        string service,
        string? environment,
        MetricSpan span,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(span, clock.GetUtcNow());
        var now = window.End;
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(
                    window.Start,
                    Names: MetricNames.Service,
                    ServiceName: service,
                    Environment: environment),
                cancellationToken))
            .Where(one => one.Environment == environment)
            .ToList();

        if (series.Count == 0)
        {
            return null;
        }

        return ServiceView.Detail(
            service,
            environment,
            await _reader.ReadAsync(series, window, cancellationToken),
            await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken));
    }

    /// <summary>Lists the metric names seen within a window, for the browser.</summary>
    /// <param name="text">Only names that contain this text; null for all.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The names, alphabetically.</returns>
    public Task<IReadOnlyList<MetricName>> NamesAsync(
        string? text,
        MetricSpan span,
        CancellationToken cancellationToken) =>
        search.ListNamesAsync(text, MetricWindow.Of(span, clock.GetUtcNow()).Start, cancellationToken);

    /// <summary>Charts one metric, as one line or split by an attribute.</summary>
    /// <param name="name">The metric name.</param>
    /// <param name="show">
    /// <c>value</c>, <c>rate</c>, <c>p50</c>, <c>p95</c>, or <c>p99</c>; null picks what suits the kind.
    /// </param>
    /// <param name="split">An attribute to split by, or null for one line.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="service">Only this service's series.</param>
    /// <param name="host">Only this host's series.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The chart, or null when no series has the name within the window.</returns>
    public async Task<MetricChartResponse?> ChartAsync(
        string name,
        string? show,
        string? split,
        MetricSpan span,
        string? service,
        string? host,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(span, clock.GetUtcNow());
        var data = await _reader.ReadAsync(
            new MetricSeriesQuery(window.Start, Names: [name], ServiceName: service, HostName: host),
            window,
            cancellationToken);
        var first = data.Series.Count > 0 ? data.Series[0] : null;

        if (first is null)
        {
            return null;
        }

        var mode = Mode(first, show);
        var keys = data.Series
            .SelectMany(series => series.Attributes.Keys.Concat(["service.name", "host.name"]))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var groups = data.Series.GroupBy(
            series => split is null ? name : SplitValue(series, split),
            StringComparer.Ordinal);

        var lines = groups
            .Select(group => window.Line(group.Key, Values(data, group.ToList(), first, mode)))
            .OrderByDescending(line => line.Latest ?? double.MinValue)
            .Take(MaxLines)
            .ToList();

        return new MetricChartResponse(
            name,
            first.Kind,
            mode == "rate" ? first.Unit + "/s" : first.Unit,
            mode,
            split,
            keys,
            (int)window.Bucket.TotalSeconds,
            lines)
        {
            From = window.From,
            To = window.End,
        };
    }

    private static bool Belongs(MetricSeries series, string service, string? environment) =>
        series.ServiceName == service && series.Environment == environment;

    private static string Mode(MetricSeries series, string? show) => (series.Kind, show) switch
    {
        (MetricKind.Histogram, "rate" or "p50" or "p95" or "p99") => show!,
        (MetricKind.Histogram, _) => "p95",
        (MetricKind.Sum, _) when series.Monotonic => "rate",
        _ => "value",
    };

    private static Dictionary<DateTimeOffset, double> Values(
        SeriesData data,
        List<MetricSeries> series,
        MetricSeries first,
        string mode)
    {
        switch (mode)
        {
            case "rate" when first.Kind == MetricKind.Histogram:
                return data.Increases(series).ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Count / data.Window.Bucket.TotalSeconds);
            case "rate":
                return data.Rates(series);
            case "p50" or "p95" or "p99":
                var quantile = mode == "p50" ? 0.5 : mode == "p95" ? 0.95 : 0.99;

                return data.Increases(series)
                    .Select(pair => (pair.Key, Value: pair.Value.Quantile(quantile)))
                    .Where(pair => pair.Value is not null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value!.Value);
            default:
                // Gauges are averaged over series; sums that can fall, such as memory by state, add up.
                var levels = data.Levels(series);

                return first.Kind == MetricKind.Gauge
                    ? levels.ToDictionary(pair => pair.Key, pair => pair.Value / series.Count)
                    : levels;
        }
    }

    private static string SplitValue(MetricSeries series, string key) => key switch
    {
        "service.name" => series.ServiceName ?? "(none)",
        "host.name" => series.HostName ?? "(none)",
        _ => series.Attributes.GetValueOrDefault(key) ?? "(none)",
    };
}
