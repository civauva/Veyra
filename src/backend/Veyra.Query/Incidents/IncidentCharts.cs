using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Incidents.Api;
using Veyra.Query.Metrics;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Incidents;

/// <summary>
/// Draws the charts of an incident's affected blocks over its window (ADR 0026), chosen by the kind of block: a
/// service's error rate and p95 latency, a host's or container's CPU and memory, a database server's connections and
/// blocked sessions, and its waits. The figures are read as the blocks' own screens read them.
/// </summary>
/// <param name="search">The stored metrics.</param>
/// <param name="logs">The stored logs, for a service's error rate, which is what the error-rate rule measures.</param>
internal sealed class IncidentCharts(IMetricSearch search, ILogSearch logs)
{
    /// <summary>The most charts an incident shows.</summary>
    public const int MaxCharts = 4;

    // Enough points for a smooth line over an incident's window, few enough to read quickly.
    private const int _points = 120;

    private readonly MetricReader _reader = new(search);

    /// <summary>Draws the charts, the first affected block's first, until there are four.</summary>
    /// <param name="blocks">The affected blocks, in the order their alerts joined.</param>
    /// <param name="environment">The incident's environment.</param>
    /// <param name="window">The incident's window.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>At most four charts; a block without data draws none.</returns>
    public async Task<IReadOnlyList<IncidentChartResponse>> ReadAsync(
        IReadOnlyList<AlertSubject> blocks,
        string? environment,
        IncidentWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(window);

        var lines = MetricWindow.Covering(window.From, window.To, _points);
        var charts = new List<IncidentChartResponse>();

        foreach (var block in blocks)
        {
            if (charts.Count >= MaxCharts)
            {
                break;
            }

            var drawn = block.Kind switch
            {
                AlertBlockKind.Service => await ServiceAsync(block, environment, lines, cancellationToken),
                AlertBlockKind.Host => await HostAsync(block, environment, lines, cancellationToken),
                AlertBlockKind.Container => await ContainerAsync(block, environment, lines, cancellationToken),
                _ => await DatabaseAsync(block, environment, lines, cancellationToken),
            };

            charts.AddRange(drawn.Take(MaxCharts - charts.Count));
        }

        return charts;
    }

    private async Task<List<IncidentChartResponse>> ServiceAsync(
        AlertSubject block,
        string? environment,
        MetricWindow window,
        CancellationToken cancellationToken)
    {
        var charts = new List<IncidentChartResponse>();
        var shown = IncidentBlockResponse.Of(block, environment);

        // Records that name no environment cannot be told apart in a query, so an incident without one counts every
        // environment of the service, as its alerts do.
        var summary = await logs.SummarizeAsync(
            new LogQuery(
                From: window.Start,
                To: window.End,
                MinimumSeverity: null,
                Services: [block.Name],
                Text: null,
                TraceId: null,
                Limit: 1,
                After: null,
                Environments: environment is null ? null : [environment]),
            window.Bucket,
            cancellationToken);

        if (summary.Buckets.Any(bucket => bucket.Total > 0))
        {
            var ratios = summary.Buckets
                .Where(bucket => bucket.Total > 0)
                .GroupBy(bucket => bucket.Start)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(bucket => bucket.Errors) / (double)group.Sum(bucket => bucket.Total));

            charts.Add(new IncidentChartResponse("Error rate", shown, "ratio", [window.Line("errors", ratios)]));
        }

        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(
                    window.Start,
                    Names: [MetricNames.RequestDuration],
                    ServiceName: block.Name,
                    Environment: environment),
                cancellationToken))
            .Where(one => one.Environment == environment)
            .ToList();

        if (series.Count > 0)
        {
            var data = await _reader.ReadAsync(series, window, cancellationToken);
            var p95 = ServiceView.Quantiles(data, 0.95).ToDictionary(pair => pair.Key, pair => pair.Value * 1000);

            charts.Add(new IncidentChartResponse("Latency p95", shown, "ms", [window.Line("p95", p95)]));
        }

        return charts;
    }

    private async Task<List<IncidentChartResponse>> HostAsync(
        AlertSubject block,
        string? environment,
        MetricWindow window,
        CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(
                    window.Start,
                    Names: [MetricNames.CpuTime, MetricNames.Memory],
                    HostName: block.Name),
                cancellationToken))
            .Where(one => one.ServiceName is null && !ContainerView.IsContainer(one))
            .ToList();

        if (series.Count == 0)
        {
            return [];
        }

        var data = await _reader.ReadAsync(series, window, cancellationToken);
        var shown = IncidentBlockResponse.Of(block, environment);

        return
        [
            new IncidentChartResponse("CPU", shown, "ratio", [window.Line("busy", HostView.CpuBusy(data))]),
            new IncidentChartResponse("Memory", shown, "ratio", [window.Line("used", HostView.MemoryUsed(data))]),
        ];
    }

    private async Task<List<IncidentChartResponse>> ContainerAsync(
        AlertSubject block,
        string? environment,
        MetricWindow window,
        CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(
                    window.Start,
                    Names: [MetricNames.ContainerCpu, MetricNames.ContainerMemoryUsage],
                    HostName: block.Part,
                    Block: new TelemetryBlock(AlertBlockKind.Container, block.Name)),
                cancellationToken))
            .Where(ContainerView.IsContainer)
            .ToList();

        if (series.Count == 0)
        {
            return [];
        }

        var data = await _reader.ReadAsync(series, window, cancellationToken);
        var shown = IncidentBlockResponse.Of(block, environment);

        return
        [
            new IncidentChartResponse(
                "CPU",
                shown,
                "percent",
                [window.Line("cpu", data.Levels(data.Named(MetricNames.ContainerCpu)))]),
            new IncidentChartResponse(
                "Memory",
                shown,
                "bytes",
                [window.Line("used", data.Levels(data.Named(MetricNames.ContainerMemoryUsage)))]),
        ];
    }

    private async Task<List<IncidentChartResponse>> DatabaseAsync(
        AlertSubject block,
        string? environment,
        MetricWindow window,
        CancellationToken cancellationToken)
    {
        var server = new TelemetryBlock(AlertBlockKind.Database, block.Name);
        var postgres = server.Engine == "postgresql";
        var (connections, blocked) = postgres
            ? (MetricNames.PostgresBackends, MetricNames.PostgresBlocked)
            : (SqlServerNames.Connections, SqlServerNames.Blocked);
        string[] names = postgres ? [connections, blocked] : [connections, blocked, SqlServerNames.WaitTime];

        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(window.Start, Names: names, Block: server),
            cancellationToken);

        if (series.Count == 0)
        {
            return [];
        }

        var data = await _reader.ReadAsync(series, window, cancellationToken);
        var shown = IncidentBlockResponse.Of(block, environment);
        var charts = new List<IncidentChartResponse>
        {
            new(
                "Connections and blocked sessions",
                shown,
                "count",
                [
                    window.Line("connections", data.Levels(data.Named(connections))),
                    window.Line("blocked", data.Levels(data.Named(blocked))),
                ]),
        };

        // Seconds waited per second, shown as milliseconds per second like the server's own waits chart reads.
        var waits = DatabaseView.Waits(data).Select(Milliseconds).ToList();

        if (waits.Count > 0)
        {
            charts.Add(new IncidentChartResponse("Waits", shown, "ms", waits));
        }

        return charts;
    }

    private static ChartLineResponse Milliseconds(ChartLineResponse line) => new(
        line.Label,
        line.Latest * 1000,
        [.. line.Points.Select(point => point with { Value = point.Value * 1000 })]);
}
