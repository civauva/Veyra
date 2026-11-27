namespace Veyra.Query.Incidents.Api;

/// <summary>What surrounds an incident, from traces, logs, metrics, and database records (ADR 0026).</summary>
/// <param name="From">The start of the window: 30 minutes before the incident opened.</param>
/// <param name="To">Its end: 10 minutes after the incident resolved, or now.</param>
/// <param name="ExecutionPath">The path that failed, when a failing trace or job run was found.</param>
/// <param name="Exception">The exception behind it, when one was found.</param>
/// <param name="Timeline">Failed job runs, configuration changes, deadlocks, and first errors, oldest first.</param>
/// <param name="Charts">At most 4 charts of the affected blocks.</param>
/// <param name="Logs">Warnings and worse from the affected blocks.</param>
/// <param name="Components">The affected blocks, then the blocks related to them.</param>
public sealed record IncidentContextResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    ExecutionPathResponse? ExecutionPath,
    ObservedExceptionResponse? Exception,
    IReadOnlyList<IncidentSignalResponse> Timeline,
    IReadOnlyList<IncidentChartResponse> Charts,
    IncidentLogsResponse Logs,
    IReadOnlyList<IncidentComponentResponse> Components);
