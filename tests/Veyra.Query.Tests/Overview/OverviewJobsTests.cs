using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;
using Veyra.Query.Jobs;
using Veyra.Query.Overview;
using Veyra.Query.Overview.Api;

namespace Veyra.Query.Tests.Overview;

/// <summary>The jobs figure and the pipeline runs, across servers and services (ADR 0023, point 4; ADR 0027).</summary>
public class OverviewJobsTests
{
    private static readonly DateTimeOffset _end = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly TelemetryBlock _prod = TelemetryBlock.Database("microsoft.sql_server", "SQL-PROD-01");

    private static readonly TelemetryBlock _report = TelemetryBlock.Database("microsoft.sql_server", "SQL-RPT-01");

    private static readonly TelemetryBlock _billing = new(AlertBlockKind.Service, "billing");

    [Fact]
    public void Figure_Jobs_IsTheShareOfFinishedRunsThatSucceeded()
    {
        JobSummary[] jobs =
        [
            Summary(_prod, "Nightly backup", -2, JobRunStatus.Failed, succeeded: 1, failed: 1),
            Summary(_report, "Load", -1, JobRunStatus.Succeeded, succeeded: 2, failed: 0),
            Summary(_billing, "nightly-import", -1, JobRunStatus.Succeeded, succeeded: 5, failed: 0, running: 1),
        ];

        var figure = OverviewJobs.Figure(jobs);

        Assert.Equal((8.0 / 9, 9, 1), (figure.SucceededRatio!.Value, figure.Runs, figure.Failed));
    }

    [Fact]
    public void Figure_NoRuns_HasNoRatio()
    {
        var figure = OverviewJobs.Figure([]);

        Assert.Equal((null, 0, 0), (figure.SucceededRatio, figure.Runs, figure.Failed));
    }

    [Fact]
    public void Pipelines_LatestRunOfEachJob_FailedFirstThenLatestFirst()
    {
        JobEntry[] jobs =
        [
            Entry(Summary(_prod, "Nightly backup", -1, JobRunStatus.Succeeded, succeeded: 1, failed: 1)),
            Entry(Summary(_prod, "Index rebuild", -4, JobRunStatus.Failed, succeeded: 0, failed: 1)),
            Entry(Summary(_report, "Load", -2, JobRunStatus.Succeeded, succeeded: 1, failed: 0)),
            Entry(Summary(_report, "Purge", -6, JobRunStatus.Failed, succeeded: 0, failed: 1)),
            Entry(Summary(_billing, "nightly-import", -3, JobRunStatus.Failed, succeeded: 3, failed: 1)),
        ];

        var pipelines = OverviewJobs.Pipelines(jobs, _end);

        Assert.Equal(
            [
                ("nightly-import", PipelineStatus.Failed),
                ("Index rebuild", PipelineStatus.Failed),
                ("Purge", PipelineStatus.Failed),
                ("Nightly backup", PipelineStatus.Succeeded),
                ("Load", PipelineStatus.Succeeded),
            ],
            pipelines.Select(pipeline => (pipeline.Name, pipeline.Status)));
        var backup = pipelines[3];
        Assert.Equal(("SQL-PROD-01", "microsoft.sql_server"), (backup.Server, backup.Engine));
        Assert.Equal((_end.AddHours(-1), 2, 1), (backup.LastRunAt, backup.Runs, backup.FailedRuns));
        Assert.Equal(JobIds.Encode(jobs[0].Job), backup.JobId);
        var import = pipelines[0];
        Assert.Equal(("billing", null, 4, 1), (import.Server, import.Engine, import.Runs, import.FailedRuns));
    }

    [Fact]
    public void Pipelines_JobRunningNow_IsRunningSinceItStarted()
    {
        var summary = Summary(_prod, "Nightly backup", -24, JobRunStatus.Succeeded, succeeded: 1, failed: 0);
        var entry = new JobEntry(summary.Job, summary, new RunningJob(summary.Job, 90));

        var pipeline = Assert.Single(OverviewJobs.Pipelines([entry], _end));

        Assert.Equal(PipelineStatus.Running, pipeline.Status);
        Assert.Equal((_end.AddSeconds(-90), 90d, 1), (pipeline.LastRunAt, pipeline.DurationSeconds, pipeline.Runs));
    }

    [Fact]
    public void Pipelines_ManyJobs_AreCutAtEight()
    {
        var jobs = Enumerable.Range(1, 12)
            .Select(hour => Entry(Summary(_prod, $"Job {hour}", -hour, JobRunStatus.Succeeded, 1, 0)))
            .ToList();

        var pipelines = OverviewJobs.Pipelines(jobs, _end);

        Assert.Equal(8, pipelines.Count);
        Assert.Equal("Job 1", pipelines[0].Name);
    }

    [Fact]
    public void Pipelines_RowsAsked_TakesThatMany()
    {
        var jobs = Enumerable.Range(1, 30)
            .Select(hour => Entry(Summary(_prod, $"Job {hour}", -hour, JobRunStatus.Succeeded, 1, 0)))
            .ToList();

        var pipelines = OverviewJobs.Pipelines(jobs, _end, most: 20);

        Assert.Equal(20, pipelines.Count);
        Assert.Equal("Job 1", pipelines[0].Name);
    }

    private static JobEntry Entry(JobSummary summary) => new(summary.Job, summary, null);

    private static JobSummary Summary(
        TelemetryBlock block,
        string job,
        int hours,
        JobRunStatus last,
        int succeeded,
        int failed,
        int running = 0) => new(
        new JobKey(
            block.Kind == AlertBlockKind.Database ? JobSource.SqlAgent : JobSource.Trace,
            null,
            block,
            job),
        succeeded + failed + running,
        succeeded,
        failed,
        new JobDuration(_end.AddHours(hours), 30, last),
        [last],
        30,
        30,
        30,
        null);
}
