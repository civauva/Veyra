namespace Veyra.Query.Metrics.Api;

/// <summary>The figures a database server's Overview tab tells beside its weather, worked out on the server (VEY-68).</summary>
/// <param name="TopWait">The wait that took the most time over the range, with its share; null when nothing waited.</param>
/// <param name="LateBackups">The databases, tempdb aside, without a full backup within 26 hours.</param>
/// <param name="FailedJobs">The jobs whose last run failed.</param>
/// <param name="OldestWraparound">
/// The database oldest to transaction ID wraparound (PostgreSQL); null where the engine has none.
/// </param>
public sealed record DatabaseOverviewResponse(
    WaitShareResponse? TopWait,
    IReadOnlyList<string> LateBackups,
    IReadOnlyList<AgentJobResponse> FailedJobs,
    DatabasePartResponse? OldestWraparound);
