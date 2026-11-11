using Veyra.Core.Metrics;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Puts together the Metrics screens: the overview of hosts and services, one host, one service, and one metric for
/// the browser. Each view reads its series once for the chosen window and once for the last five minutes.
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

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds the overview.</summary>
    /// <param name="range">The window of the lines, for example <c>6h</c>.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The overview.</returns>
    public async Task<MetricsOverviewResponse> OverviewAsync(string? range, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var window = MetricWindow.Of(range, now);
        var recent = MetricWindow.Recent(now);

        var hostSeries = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: _hostCard),
                cancellationToken))
            .Where(series => series.HostName is not null && series.ServiceName is null)
            .ToList();
        var hostWindow = await _reader.ReadAsync(
            hostSeries.Where(series => series.Name == MetricNames.CpuTime).ToList(),
            window,
            cancellationToken);
        var hostRecent = await _reader.ReadAsync(hostSeries, recent, cancellationToken);
        var hostDay = await _reader.ReadAsync(
            hostSeries.Where(series => series.Name == MetricNames.Filesystem).ToList(),
            MetricWindow.Day(now),
            cancellationToken);

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

        var serviceSeries = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: _serviceCard),
                cancellationToken))
            .Where(series => series.ServiceName is not null)
            .ToList();
        var serviceWindow = await _reader.ReadAsync(
            serviceSeries.Where(series => series.Name == MetricNames.RequestDuration).ToList(),
            window,
            cancellationToken);
        var serviceRecent = await _reader.ReadAsync(serviceSeries, recent, cancellationToken);

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

        var usage = await search.GetUsageAsync(now, cancellationToken);
        var names = await search.ListNamesAsync(null, window.Start, cancellationToken);

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
            hosts,
            services);
    }

    /// <summary>Builds one host's view.</summary>
    /// <param name="host">The host name.</param>
    /// <param name="range">The window, for example <c>6h</c>.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The view, or null when the host sent no host metrics within the window.</returns>
    public async Task<HostMetricsResponse?> HostAsync(string host, string? range, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var window = MetricWindow.Of(range, now);
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(window.Start, Names: MetricNames.Host, HostName: host),
                cancellationToken))
            .Where(one => one.ServiceName is null)
            .ToList();

        if (series.Count == 0)
        {
            return null;
        }

        return HostView.Detail(
            host,
            await _reader.ReadAsync(series, window, cancellationToken),
            await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken),
            await _reader.ReadAsync(
                series.Where(one => one.Name == MetricNames.Filesystem).ToList(),
                MetricWindow.Day(now),
                cancellationToken));
    }

    /// <summary>Builds one service's view.</summary>
    /// <param name="service">The service name.</param>
    /// <param name="environment">The environment; null for a service that names none.</param>
    /// <param name="range">The window, for example <c>6h</c>.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The view, or null when the service sent none of these metrics within the window.</returns>
    public async Task<ServiceMetricsResponse?> ServiceAsync(
        string service,
        string? environment,
        string? range,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var window = MetricWindow.Of(range, now);
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
    /// <param name="range">The window, for example <c>6h</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The names, alphabetically.</returns>
    public Task<IReadOnlyList<MetricName>> NamesAsync(
        string? text,
        string? range,
        CancellationToken cancellationToken) =>
        search.ListNamesAsync(text, MetricWindow.Of(range, clock.GetUtcNow()).Start, cancellationToken);

    /// <summary>Charts one metric, as one line or split by an attribute.</summary>
    /// <param name="name">The metric name.</param>
    /// <param name="show">
    /// <c>value</c>, <c>rate</c>, <c>p50</c>, <c>p95</c>, or <c>p99</c>; null picks what suits the kind.
    /// </param>
    /// <param name="split">An attribute to split by, or null for one line.</param>
    /// <param name="range">The window, for example <c>6h</c>.</param>
    /// <param name="service">Only this service's series.</param>
    /// <param name="host">Only this host's series.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The chart, or null when no series has the name within the window.</returns>
    public async Task<MetricChartResponse?> ChartAsync(
        string name,
        string? show,
        string? split,
        string? range,
        string? service,
        string? host,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(range, clock.GetUtcNow());
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
            lines);
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
