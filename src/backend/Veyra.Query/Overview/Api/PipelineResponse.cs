namespace Veyra.Query.Overview.Api;

/// <summary>
/// A job with its latest run (ADR 0023, point 4): a SQL Server Agent job (ADR 0020, area 10) or a job recognised in
/// traces (ADR 0027).
/// </summary>
/// <param name="Name">The job.</param>
/// <param name="Server">
/// The server, as <c>server.address</c> names it, for a SQL Server Agent job; the service for a traced job.
/// </param>
/// <param name="Engine">The engine, as <c>db.system.name</c> spells it; null for a traced job.</param>
/// <param name="LastRunAt">When its latest run started.</param>
/// <param name="DurationSeconds">How long the run took, or for a run going on, how long it has run so far.</param>
/// <param name="Status">How the run stands.</param>
/// <param name="Runs">How many runs of the job started in the window.</param>
/// <param name="FailedRuns">How many of them failed.</param>
/// <param name="JobId">The job's ID, for its screen at <c>/jobs/{id}</c> and <c>GET /api/v1/jobs/{id}</c>.</param>
public sealed record PipelineResponse(
    string Name,
    string Server,
    string? Engine,
    DateTimeOffset LastRunAt,
    double DurationSeconds,
    PipelineStatus Status,
    int Runs,
    int FailedRuns,
    string JobId);
