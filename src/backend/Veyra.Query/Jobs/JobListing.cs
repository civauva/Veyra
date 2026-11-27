using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Query.Jobs.Api;
using Veyra.Query.Systems;

namespace Veyra.Query.Jobs;

/// <summary>
/// Works out the Jobs list (ADR 0027) from each job's runs of the window and the jobs that run now: which jobs it
/// keeps, their order, their rows, and their figures. Pure; tested without a database.
/// </summary>
internal static class JobListing
{
    /// <summary>One entry per job: those with runs in the window, and those that run now.</summary>
    /// <param name="summaries">Each job's runs of the window, added up.</param>
    /// <param name="running">The SQL Server Agent jobs that run at the end of the window.</param>
    /// <param name="search">
    /// The text a job's name or block must contain, in any case; the summaries were searched with it already, the
    /// running jobs are searched here.
    /// </param>
    /// <returns>The entries, in no particular order.</returns>
    public static List<JobEntry> Merge(
        IEnumerable<JobSummary> summaries,
        IEnumerable<RunningJob> running,
        string? search)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(running);

        var now = running
            .Where(job => Matches(job.Job, search))
            .GroupBy(job => job.Job)
            .ToDictionary(group => group.Key, group => group.First());
        var entries = summaries
            .Select(summary => new JobEntry(summary.Job, summary, now.GetValueOrDefault(summary.Job)))
            .ToList();
        var known = entries.Select(entry => entry.Job).ToHashSet();

        entries.AddRange(now.Values
            .Where(job => !known.Contains(job.Job))
            .Select(job => new JobEntry(job.Job, null, job)));

        return entries;
    }

    /// <summary>
    /// The entries asked for: the jobs SQL Server set up for itself left out unless asked for, as the server's Jobs tab
    /// leaves them out (VEY-66), and those of the status asked for.
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <param name="system">Whether SQL Server's own jobs are kept.</param>
    /// <param name="status">Which jobs to keep by how they stand.</param>
    /// <returns>The entries kept.</returns>
    public static List<JobEntry> Keep(IEnumerable<JobEntry> entries, bool system, JobStatusFilter status)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return
        [
            .. entries
                .Where(entry => system || !IsSystem(entry.Job))
                .Where(entry => status switch
                {
                    JobStatusFilter.Failing => entry.LastStatus == JobRunStatus.Failed,
                    JobStatusFilter.Running => entry.IsRunning,
                    _ => true,
                }),
        ];
    }

    /// <summary>
    /// The entries in the list's order: those whose latest run failed, then those running, then by name.
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <returns>The entries, ordered.</returns>
    public static List<JobEntry> Ordered(IEnumerable<JobEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return
        [
            .. entries
                .OrderBy(entry => entry.LastStatus switch
                {
                    JobRunStatus.Failed => 0,
                    JobRunStatus.Running => 1,
                    _ => 2,
                })
                .ThenBy(entry => entry.Job.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Job.Block.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Job.Environment, StringComparer.Ordinal),
        ];
    }

    /// <summary>What the entries add up to.</summary>
    /// <param name="entries">The entries.</param>
    /// <returns>The figures; without finished runs, the ratio is null.</returns>
    public static JobListFiguresResponse Figures(IReadOnlyCollection<JobEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var succeeded = entries.Sum(entry => entry.Summary?.Succeeded ?? 0);
        var failed = entries.Sum(entry => entry.Summary?.Failed ?? 0);

        return new JobListFiguresResponse(
            entries.Count,
            entries.Sum(entry => entry.Summary?.Runs ?? 0),
            failed,
            Ratio(succeeded, failed),
            entries.Count(entry => entry.IsRunning));
    }

    /// <summary>The row of one entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="end">The end of the window, from which a running job's start is worked out.</param>
    /// <returns>The row.</returns>
    public static JobListItemResponse Item(JobEntry entry, DateTimeOffset end)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var job = entry.Job;
        var summary = entry.Summary;
        var subject = new AlertSubject(job.Block.Kind, job.Block.Name);
        var recent = (summary?.Recent ?? []).Select(status => JobText.Status(status)).ToList();
        JobLastRunResponse? last = null;

        if (entry.Running is { } running)
        {
            // The run going on is not in the history yet; it is the latest run, and the strip ends with it.
            last = new JobLastRunResponse(end.AddSeconds(-running.Seconds), null, JobText.Status(JobRunStatus.Running));
            recent.Add(JobText.Status(JobRunStatus.Running));
        }
        else if (summary is not null)
        {
            last = new JobLastRunResponse(
                summary.LastRun.StartedAt,
                summary.LastRun.DurationSeconds,
                JobText.Status(summary.LastRun.Status));
        }

        return new JobListItemResponse(
            JobIds.Encode(job),
            job.Name,
            JobText.Source(job.Source),
            new JobBlockResponse(subject.KindName, subject.Name, null, subject.BlockId(job.Environment)),
            job.Environment,
            last,
            summary?.Runs ?? 0,
            summary?.Failed ?? 0,
            summary is null ? null : Ratio(summary.Succeeded, summary.Failed),
            summary?.UsualSeconds,
            [.. recent.TakeLast(JobSummary.RecentRuns)],
            entry.IsRunning);
    }

    /// <summary>Whether a job's name or block contains a text, in any case.</summary>
    /// <param name="job">The job.</param>
    /// <param name="search">The text; every job matches none.</param>
    /// <returns>Whether it matches.</returns>
    public static bool Matches(JobKey job, string? search)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var text = search.Trim();

        return job.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || job.Block.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The share of finished runs that succeeded.</summary>
    /// <param name="succeeded">The runs that succeeded.</param>
    /// <param name="failed">The runs that failed.</param>
    /// <returns>The share, or null without finished runs.</returns>
    public static double? Ratio(int succeeded, int failed) =>
        succeeded + failed == 0 ? null : (double)succeeded / (succeeded + failed);

    // A job SQL Server set up for a feature of its own, such as syspolicy_purge_history; a traced job never is one.
    private static bool IsSystem(JobKey job) => job.Source == JobSource.SqlAgent && SystemObjects.IsSystemJob(job.Name);
}
