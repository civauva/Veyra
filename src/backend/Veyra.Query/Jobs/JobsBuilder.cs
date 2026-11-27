using Veyra.Core.Activity;
using Veyra.Core.Jobs;
using Veyra.Core.Metrics;
using Veyra.Query.Jobs.Api;
using Veyra.Query.Metrics;

namespace Veyra.Query.Jobs;

/// <summary>
/// Builds the Jobs list and a job's screen (ADR 0027) from the job runs the Storage module keeps, the SQL Server Agent
/// jobs that run now, and, for a SQL Server Agent run, its steps in the job history.
/// </summary>
/// <remarks>
/// Each job's runs are added up in the database, so the list costs what its number of jobs does, not its runs. A job's
/// screen reads at most <see cref="MaxRuns"/> runs and <see cref="MaxDurations"/> durations, newest first.
/// </remarks>
/// <param name="jobs">The stored job runs.</param>
/// <param name="activity">The job history of each SQL Server, for the steps of its runs.</param>
/// <param name="search">The stored metrics, for the jobs that run now.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class JobsBuilder(
    IJobRuns jobs,
    IDatabaseActivitySearch activity,
    IMetricSearch search,
    TimeProvider clock)
{
    /// <summary>The most runs a job's screen lists.</summary>
    public const int MaxRuns = 200;

    /// <summary>The most runs a job's duration chart draws, the newest.</summary>
    public const int MaxDurations = 5_000;

    // Every step of 200 runs of a job with many steps.
    private const int _maxStepRecords = 10_000;

    private readonly RunningJobs _running = new(search);

    /// <summary>Builds the Jobs list.</summary>
    /// <param name="period">The window.</param>
    /// <param name="environment">The environment; every one when null or blank.</param>
    /// <param name="source">Only jobs of this source; both when null.</param>
    /// <param name="text">Only jobs whose name or block contains this text, in any case; every job when null.</param>
    /// <param name="status">Which jobs to keep by how they stand.</param>
    /// <param name="system">Whether the jobs SQL Server set up for itself are listed.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The list.</returns>
    public async Task<JobListResponse> ListAsync(
        ResolvedSpan period,
        string? environment,
        JobSource? source,
        string? text,
        JobStatusFilter status,
        bool system,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);

        var chosen = string.IsNullOrWhiteSpace(environment) ? null : environment;
        var summarizing = jobs.SummarizeAsync(
            new JobQuery(period.From, period.To, chosen, source, text),
            cancellationToken);
        var reading = source == JobSource.Trace
            ? Task.FromResult<IReadOnlyList<RunningJob>>([])
            : _running.ReadAsync(chosen, period.To, cancellationToken);

        await Task.WhenAll(summarizing, reading);

        var entries = JobListing.Keep(
            JobListing.Merge(await summarizing, await reading, text),
            system,
            JobStatusFilter.All);

        return new JobListResponse(
            period.From,
            period.To,
            JobListing.Figures(entries),
            [
                .. JobListing.Ordered(JobListing.Keep(entries, system: true, status))
                    .Select(entry => JobListing.Item(entry, period.To)),
            ]);
    }

    /// <summary>Builds one job's screen.</summary>
    /// <param name="job">The job.</param>
    /// <param name="period">The window.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The screen, or null when Veyra knows no run of the job and it does not run now.</returns>
    public async Task<JobDetailResponse?> GetAsync(JobKey job, ResolvedSpan period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(period);

        var summarizing = jobs.SummarizeAsync(new JobQuery(period.From, period.To, Job: job), cancellationToken);
        var listing = jobs.ListRunsAsync(job, period.From, period.To, false, MaxRuns, cancellationToken);
        var charting = jobs.ListDurationsAsync(job, period.From, period.To, MaxDurations, cancellationToken);
        var reading = job.Source == JobSource.SqlAgent
            ? _running.ReadAsync(job.Environment, period.To, cancellationToken)
            : Task.FromResult<IReadOnlyList<RunningJob>>([]);

        await Task.WhenAll(summarizing, listing, charting, reading);

        var summary = await summarizing is [var only, ..] ? only : null;
        var running = (await reading).FirstOrDefault(one => one.Job == job);

        // A job without runs in this window is still the job: its latest run is shown from before the window.
        if (summary is null && running is null)
        {
            var latest = await jobs.ListRunsAsync(
                job,
                DateTimeOffset.UnixEpoch,
                clock.GetUtcNow().AddDays(1),
                false,
                1,
                cancellationToken);

            if (latest.Count == 0)
            {
                return null;
            }

            summary = new JobSummary(
                job,
                0,
                0,
                0,
                new JobDuration(latest[0].StartedAt, latest[0].DurationSeconds, latest[0].Status),
                [],
                null,
                null,
                null,
                null);
        }

        var runs = await RunsAsync(job, await listing, cancellationToken);
        var item = JobListing.Item(new JobEntry(job, summary, running), period.To);

        return new JobDetailResponse(
            period.From,
            period.To,
            item,
            new JobFiguresResponse(
                item.Runs,
                item.Failed,
                item.SuccessRatio,
                summary?.MedianSeconds,
                summary?.P95Seconds,
                summary?.RecordsProcessed),
            [
                .. (await charting)
                    .Reverse()
                    .Select(run => new JobDurationPointResponse(
                        run.StartedAt,
                        run.DurationSeconds,
                        JobText.Status(run.Status))),
            ],
            runs);
    }

    // The runs with their steps: a traced run keeps its own; a SQL Server Agent run's are read from its job history.
    private async Task<IReadOnlyList<JobRunDetailResponse>> RunsAsync(
        JobKey job,
        IReadOnlyList<JobRun> runs,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ActivityRecord> records = [];

        if (job.Source == JobSource.SqlAgent && runs.Count > 0)
        {
            records = await activity.ListRecordsMatchingAsync(
                job.Block.Name,
                AgentSteps.Kind,
                runs.Min(run => run.StartedAt.AddSeconds(-(run.DurationSeconds ?? 0) - 1)),
                runs.Max(run => run.StartedAt.AddSeconds((run.DurationSeconds ?? 0) + 1)),
                new ActivityAttributeMatch("job_name", [job.Name], []),
                _maxStepRecords,
                cancellationToken);
        }

        return
        [
            .. runs.Select(run =>
            {
                var (steps, retries) = job.Source == JobSource.SqlAgent
                    ? AgentSteps.For(run, records)
                    : (run.Steps, run.Retries);

                return new JobRunDetailResponse(
                    run.RunId,
                    run.StartedAt,
                    run.DurationSeconds,
                    JobText.Status(run.Status),
                    run.Message,
                    run.TraceId,
                    run.TraceKept,
                    run.RecordsProcessed,
                    run.RecordsTotal,
                    retries ?? run.Retries,
                    [
                        .. steps.Select(step => new JobRunStepResponse(
                            step.Name,
                            step.DurationSeconds,
                            JobText.Status(step.Status),
                            step.Message)),
                    ]);
            }),
        ];
    }
}
