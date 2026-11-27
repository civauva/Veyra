namespace Veyra.Query.Server.Api;

/// <summary>A SQL Server Agent job (ADR 0020, area 10).</summary>
/// <param name="Name">The job.</param>
/// <param name="Failed">Whether its last run failed.</param>
/// <param name="LastRun">When its last run started, or <see langword="null"/>.</param>
/// <param name="LastSeconds">How long its last run took, or <see langword="null"/>.</param>
/// <param name="RunningSeconds">How long it has run now, or <see langword="null"/> when it is not running.</param>
/// <param name="NextRun">When it runs next, or <see langword="null"/>.</param>
/// <param name="Late">Whether it has not started 15 minutes after its scheduled time.</param>
/// <param name="UsualSeconds">Its usual duration over its last 10 successful runs, or <see langword="null"/>.</param>
/// <param name="Runs">Its runs of the last week the agent saw, the newest first.</param>
public sealed record JobResponse(
    string Name,
    bool Failed,
    DateTimeOffset? LastRun,
    double? LastSeconds,
    double? RunningSeconds,
    DateTimeOffset? NextRun,
    bool Late,
    double? UsualSeconds,
    IReadOnlyList<JobRunResponse> Runs)
{
    /// <summary>Whether SQL Server set it up for a feature of its own (VEY-64), such as policy management.</summary>
    public bool System { get; init; }

    /// <summary>
    /// Whether it needs a look (VEY-67): its last run failed, it did not start when scheduled, or it has run at least
    /// five minutes and at least twice its usual time. The Simple view lists only these.
    /// </summary>
    public bool NeedsLook =>
        Failed
        || Late
        || (RunningSeconds >= 300 && UsualSeconds is { } usual && RunningSeconds >= 2 * usual);
}
