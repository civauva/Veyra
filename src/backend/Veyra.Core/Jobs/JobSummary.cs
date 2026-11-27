namespace Veyra.Core.Jobs;

/// <summary>What a job's runs of a window add up to (ADR 0027), counted in the database.</summary>
/// <param name="Job">The job.</param>
/// <param name="Runs">How many of its runs started in the window.</param>
/// <param name="Succeeded">How many of them succeeded.</param>
/// <param name="Failed">How many of them failed.</param>
/// <param name="LastRun">Its latest run of the window.</param>
/// <param name="Recent">The statuses of its latest runs, at most <see cref="RecentRuns"/>, oldest first.</param>
/// <param name="UsualSeconds">
/// The median duration of its runs that succeeded, which a failure ending early does not pull down;
/// <see langword="null"/> without any.
/// </param>
/// <param name="MedianSeconds">The median duration of its finished runs; <see langword="null"/> without any.</param>
/// <param name="P95Seconds">
/// The duration 95 of 100 finished runs did not exceed; <see langword="null"/> without any.
/// </param>
/// <param name="RecordsProcessed">
/// The records its runs processed, added up; <see langword="null"/> when none of them says.
/// </param>
public sealed record JobSummary(
    JobKey Job,
    int Runs,
    int Succeeded,
    int Failed,
    JobDuration LastRun,
    IReadOnlyList<JobRunStatus> Recent,
    double? UsualSeconds,
    double? MedianSeconds,
    double? P95Seconds,
    long? RecordsProcessed)
{
    /// <summary>How many of the latest runs a summary gives the statuses of, as a run strip draws them.</summary>
    public const int RecentRuns = 20;
}
