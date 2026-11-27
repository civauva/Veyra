namespace Veyra.Core.Jobs;

/// <summary>
/// Reads the job runs the Storage module keeps (ADR 0027), for the Jobs screens, the Overview, and incidents. Every
/// read is bounded by a window, so it reads only the days it needs.
/// </summary>
public interface IJobRuns
{
    /// <summary>Adds up each job's runs of a window.</summary>
    /// <param name="query">The window and which jobs.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One summary per job with runs in the window, in no particular order.</returns>
    Task<IReadOnlyList<JobSummary>> SummarizeAsync(JobQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Lists one job's runs of a window, newest first, with their steps and whether their trace is still kept.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="periodStart">The start of the window, inclusive.</param>
    /// <param name="periodEnd">The end of the window, exclusive.</param>
    /// <param name="failedOnly">Whether only failed runs are listed.</param>
    /// <param name="limit">The most runs.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The runs.</returns>
    Task<IReadOnlyList<JobRun>> ListRunsAsync(
        JobKey job,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        bool failedOnly,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Lists when one job's runs of a window started and how long they took, newest first.</summary>
    /// <param name="job">The job.</param>
    /// <param name="periodStart">The start of the window, inclusive.</param>
    /// <param name="periodEnd">The end of the window, exclusive.</param>
    /// <param name="limit">The most runs.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The runs' durations.</returns>
    Task<IReadOnlyList<JobDuration>> ListDurationsAsync(
        JobKey job,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        int limit,
        CancellationToken cancellationToken);
}
