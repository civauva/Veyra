using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Query.Jobs;
using Veyra.Query.Overview.Api;

namespace Veyra.Query.Overview;

/// <summary>
/// Works out the Overview's jobs figure and pipeline runs from the job runs of every source (ADR 0023, point 4;
/// ADR 0027): SQL Server Agent jobs and the jobs recognised in traces. Pure; tested without a database.
/// </summary>
internal static class OverviewJobs
{
    /// <summary>The most pipelines the Overview lists when the screen asks for no number.</summary>
    public const int MaxPipelines = 8;

    /// <summary>The share of finished runs that succeeded, and how many failed.</summary>
    /// <param name="jobs">Every job's runs of the window, added up.</param>
    /// <returns>The figure; without finished runs, its ratio is null.</returns>
    public static JobsFigureResponse Figure(IReadOnlyCollection<JobSummary> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        var succeeded = jobs.Sum(job => job.Succeeded);
        var failed = jobs.Sum(job => job.Failed);

        return new JobsFigureResponse(JobListing.Ratio(succeeded, failed), succeeded + failed, failed);
    }

    /// <summary>
    /// The latest run of each job, failed ones first, then the latest first, at most <paramref name="most"/>. A job
    /// that runs now is shown running, since it started.
    /// </summary>
    /// <param name="jobs">Every job with runs in the window or a run going on at its end.</param>
    /// <param name="end">The end of the window.</param>
    /// <param name="most">The most pipelines to list, as many as fit in the screen's tile.</param>
    /// <returns>The pipelines.</returns>
    public static IReadOnlyList<PipelineResponse> Pipelines(
        IReadOnlyCollection<JobEntry> jobs,
        DateTimeOffset end,
        int most = MaxPipelines)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        return jobs
            .Select(entry =>
            {
                var job = entry.Job;
                var summary = entry.Summary;

                // A database server is named by its server, as the Databases list names it; a service by its name.
                var server = job.Block.Kind == AlertBlockKind.Database ? job.Block.Server! : job.Block.Name;
                var runs = summary?.Runs ?? 0;
                var failed = summary?.Failed ?? 0;
                var id = JobIds.Encode(job);

                return entry.Running is { } running
                    ? new PipelineResponse(
                        job.Name,
                        server,
                        job.Block.Engine,
                        end.AddSeconds(-running.Seconds),
                        running.Seconds,
                        PipelineStatus.Running,
                        runs,
                        failed,
                        id)
                    : new PipelineResponse(
                        job.Name,
                        server,
                        job.Block.Engine,
                        summary!.LastRun.StartedAt,
                        summary.LastRun.DurationSeconds ?? (end - summary.LastRun.StartedAt).TotalSeconds,
                        summary.LastRun.Status switch
                        {
                            JobRunStatus.Succeeded => PipelineStatus.Succeeded,
                            JobRunStatus.Running => PipelineStatus.Running,
                            _ => PipelineStatus.Failed,
                        },
                        runs,
                        failed,
                        id);
            })
            .OrderBy(pipeline => pipeline.Status != PipelineStatus.Failed)
            .ThenByDescending(pipeline => pipeline.LastRunAt)
            .ThenBy(pipeline => pipeline.Server, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pipeline => pipeline.Name, StringComparer.OrdinalIgnoreCase)
            .Take(most)
            .ToList();
    }
}
