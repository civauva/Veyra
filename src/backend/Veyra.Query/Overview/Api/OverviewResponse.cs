namespace Veyra.Query.Overview.Api;

/// <summary>
/// The Console look's Overview in one read (ADR 0023): its figures, panels, and tables over one window, worked out on
/// the server from the same moment.
/// </summary>
/// <param name="From">The start of the period asked for.</param>
/// <param name="To">The end of the period.</param>
/// <param name="Figures">The header figures, each with its change against the period before where it changes.</param>
/// <param name="Activity">The HTTP requests and server errors per step, across services with HTTP metrics.</param>
/// <param name="Ingestion">The log records, spans, and metric points stored per step.</param>
/// <param name="HostTrends">The hosts' average CPU and memory and the fullest disk per step.</param>
/// <param name="Pipelines">The latest run of each SQL Server Agent job, failed ones first, at most eight.</param>
/// <param name="Collectors">
/// Veyra's agents with their state; null for anyone but an Admin, who alone may see the agents (ADR 0019).
/// </param>
/// <param name="Databases">One page of the databases of every server, worst first.</param>
public sealed record OverviewResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    OverviewFiguresResponse Figures,
    OverviewSeriesResponse<ActivityPointResponse> Activity,
    OverviewSeriesResponse<IngestionPointResponse> Ingestion,
    OverviewSeriesResponse<HostTrendPointResponse> HostTrends,
    IReadOnlyList<PipelineResponse> Pipelines,
    IReadOnlyList<CollectorResponse>? Collectors,
    OverviewDatabasesResponse Databases);
