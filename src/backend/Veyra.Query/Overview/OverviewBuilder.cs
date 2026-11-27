using Veyra.Core.Agents;
using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Jobs;
using Veyra.Query.Metrics;
using Veyra.Query.Overview.Api;
using Veyra.Query.Resources;
using Veyra.Query.Server;
using Veyra.Query.Systems;

namespace Veyra.Query.Overview;

/// <summary>
/// Builds the Console look's Overview in one read (ADR 0023): every figure, panel, and table over one window, from
/// the same moment.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is worked out twice. The services, hosts, and databases are the resource lists of ADR 0022, read whole, so
/// the Overview's figures are the lists' headers; the request activity and host trends are the buckets those lists
/// already read. The ingestion comes from the ingest counts, availability from the alert history, and the jobs from
/// the job runs of every source (ADR 0027), each job added up in the database.
/// </para>
/// <para>
/// The reads run side by side, so the Overview costs about what its slowest part does: the Services list, which reads
/// the overview cards' buckets (ADR 0015).
/// </para>
/// </remarks>
/// <param name="services">Builds the Services list.</param>
/// <param name="hosts">Builds the Hosts and containers list.</param>
/// <param name="databases">Builds the Databases list.</param>
/// <param name="counts">The ingest counts.</param>
/// <param name="alerts">The alerts, for those open now.</param>
/// <param name="history">The alert history, for availability.</param>
/// <param name="jobs">The job runs of every source.</param>
/// <param name="search">The stored metrics, for the jobs that run now.</param>
/// <param name="agents">Veyra's agents, for the collector health.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class OverviewBuilder(
    ServiceListBuilder services,
    HostListBuilder hosts,
    DatabaseListBuilder databases,
    IIngestCounts counts,
    IAlertStates alerts,
    IAlertHistory history,
    IJobRuns jobs,
    IMetricSearch search,
    IAgentStatuses agents,
    TimeProvider clock)
{
    private readonly RunningJobs _running = new(search);

    /// <summary>Builds the Overview of one environment, or of all of them.</summary>
    /// <param name="environment">The environment chosen in the top bar; all when null or blank.</param>
    /// <param name="span">The window; the controller has refused one that is too long.</param>
    /// <param name="collectors">Whether the agents are told, which only Admins may see (ADR 0019).</param>
    /// <param name="rows">How many databases and pipelines the screen's tables have room for.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The Overview.</returns>
    public async Task<OverviewResponse> BuildAsync(
        string? environment,
        MetricSpan span,
        bool collectors,
        OverviewRows rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(rows);

        var now = clock.GetUtcNow();
        var window = MetricWindow.Card(span, now);
        var query = new ResourceListQuery(
            environment,
            span,
            ResourceHealthFilter.Parse(null)!,
            Search: null,
            Sort: null,
            Cursor: null,
            Limit: int.MaxValue);
        var chosen = query.Chosen;
        var availabilityFrom = window.End - TimeSpan.FromDays(2 * OverviewAvailability.Days);

        var readingServices = services.ReadAsync(query, null, cancellationToken);
        var readingHosts = hosts.ReadAsync(query, HostListBuilder.HostKind, null, cancellationToken);
        var readingDatabases = databases.ReadAsync(query, null, system: false, cancellationToken);
        var readingCounts = counts.CountAsync(chosen, window.Start, window.End, window.Bucket, cancellationToken);
        var readingSpells = history.ListSpellsAsync(availabilityFrom, window.End, cancellationToken);
        var readingAlerts = alerts.ListAsync(now, cancellationToken);
        var readingAgents = collectors
            ? agents.ListAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<AgentStatus>>([]);
        var readingJobs = JobsAsync(window, chosen, cancellationToken);

        await Task.WhenAll(
            readingServices,
            readingHosts,
            readingDatabases,
            readingCounts,
            readingSpells,
            readingAlerts,
            readingAgents,
            readingJobs);

        var serviceList = await readingServices;
        var hostList = await readingHosts;
        var databaseList = await readingDatabases;
        var jobEntries = await readingJobs;
        var open = (await readingAlerts)
            .Where(alert => alert.ResolvedAt is null && (chosen is null || alert.Environment == chosen))
            .ToList();

        var figures = new OverviewFiguresResponse(
            Services(serviceList),
            new HostsFigureResponse(hostList.Header.Online, hostList.Header.Known),
            new DatabasesFigureResponse(databaseList.Header.Servers, databaseList.Header.Databases),
            new RequestsFigureResponse(serviceList.Header.Requests, serviceList.Header.RequestsChange),
            new ErrorRatioFigureResponse(serviceList.Header.ErrorRatio, serviceList.Header.ErrorRatioChange),
            new OpenAlertsFigureResponse(
                open.Count(alert => alert.Severity == AlertSeverity.Warning),
                open.Count(alert => alert.Severity == AlertSeverity.Caution)),
            OverviewJobs.Figure([.. jobEntries.Select(entry => entry.Summary).OfType<JobSummary>()]),
            OverviewAvailability.Figure(
                await readingSpells,
                chosen,
                window.End,
                anyServices: serviceList.Header.Total > 0));

        return new OverviewResponse(
            window.From,
            window.End,
            figures,
            Activity(window, serviceList),
            Ingestion(window, await readingCounts),
            HostTrends(window, hostList),
            OverviewJobs.Pipelines(jobEntries, window.End, rows.Pipelines),
            collectors
                ? [.. (await readingAgents).Select(agent => new CollectorResponse(
                    agent.Name,
                    agent.State,
                    agent.LastSeenAt,
                    agent.Version,
                    null))]
                : null,
            OverviewDatabases.List(databaseList.Servers, rows.Databases));
    }

    private static ServicesFigureResponse Services(ServiceListing list) => new(
        list.Header.Healthy,
        list.Header.Total,
        list.Header.Warning,
        list.Header.Caution,
        list.Header.Silent);

    private static OverviewSeriesResponse<ActivityPointResponse> Activity(MetricWindow window, ServiceListing list) =>
        new(
            (int)window.Bucket.TotalSeconds,
            [
                .. window.Starts.Select(start => list.Requests.TryGetValue(start, out var requests)
                    ? new ActivityPointResponse(
                        start,
                        (long)Math.Round(requests),
                        (long)Math.Round(list.Errors.GetValueOrDefault(start)))
                    : new ActivityPointResponse(start, null, null)),
            ]);

    private static OverviewSeriesResponse<IngestionPointResponse> Ingestion(
        MetricWindow window,
        IReadOnlyList<IngestCount> counts)
    {
        var byStep = counts.ToLookup(count => count.Start);

        long Of(DateTimeOffset start, IngestSignal signal) =>
            byStep[start].Where(count => count.Signal == signal).Sum(count => count.Count);

        return new(
            (int)window.Bucket.TotalSeconds,
            [
                .. window.Starts.Select(start => new IngestionPointResponse(
                    start,
                    Of(start, IngestSignal.Logs),
                    Of(start, IngestSignal.Spans),
                    Of(start, IngestSignal.Points))),
            ]);
    }

    private static OverviewSeriesResponse<HostTrendPointResponse> HostTrends(MetricWindow window, HostListing list)
    {
        static double? At(IReadOnlyDictionary<DateTimeOffset, double> values, DateTimeOffset start) =>
            values.TryGetValue(start, out var value) && double.IsFinite(value) ? value : null;

        return new(
            (int)window.Bucket.TotalSeconds,
            [
                .. window.Starts.Select(start => new HostTrendPointResponse(
                    start,
                    At(list.Cpu, start),
                    At(list.Memory, start),
                    At(list.Disk, start))),
            ]);
    }

    // Every job with runs in the window, of every source, and the SQL Server Agent jobs that run at its end. The jobs
    // SQL Server set up for itself are left out, as the Jobs tab leaves them out (VEY-66).
    private async Task<List<JobEntry>> JobsAsync(
        MetricWindow window,
        string? environment,
        CancellationToken cancellationToken)
    {
        var summarizing = jobs.SummarizeAsync(
            new JobQuery(window.From, window.End, environment),
            cancellationToken);
        var reading = _running.ReadAsync(environment, window.End, cancellationToken);

        await Task.WhenAll(summarizing, reading);

        return JobListing.Keep(
            JobListing.Merge(await summarizing, await reading, search: null),
            system: false,
            JobStatusFilter.All);
    }
}
