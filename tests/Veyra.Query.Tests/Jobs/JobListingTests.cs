using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;
using Veyra.Query.Jobs;

namespace Veyra.Query.Tests.Jobs;

/// <summary>The Jobs list (ADR 0027): one row per job, failing ones first, with its run strip and figures.</summary>
public class JobListingTests
{
    private static readonly DateTimeOffset _end = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly TelemetryBlock _sql = TelemetryBlock.Database("microsoft.sql_server", "SQL-PROD-01");

    private static readonly TelemetryBlock _billing = new(AlertBlockKind.Service, "billing");

    [Fact]
    public void Merge_SummariesAndJobsRunningNow_OneEntryPerJob()
    {
        var backup = Summary(_sql, "Nightly backup", JobRunStatus.Succeeded);
        var running = new RunningJob(backup.Job, 30);
        var rebuild = new RunningJob(Key(_sql, "Index rebuild"), 90);

        var entries = JobListing.Merge([backup], [running, rebuild], search: null);

        Assert.Equal(
            [("Index rebuild", false, true), ("Nightly backup", true, true)],
            entries.OrderBy(entry => entry.Job.Name).Select(e => (e.Job.Name, e.Summary is not null, e.IsRunning)));
    }

    [Fact]
    public void Merge_Search_KeepsOnlyRunningJobsWhoseNameOrBlockMatches()
    {
        var entries = JobListing.Merge(
            [],
            [new RunningJob(Key(_sql, "Index rebuild"), 90), new RunningJob(Key(_sql, "Purge"), 5)],
            search: " REBUILD ");

        Assert.Equal(["Index rebuild"], entries.Select(entry => entry.Job.Name));
        Assert.Equal(
            2,
            JobListing.Merge(
                [],
                [new RunningJob(Key(_sql, "Purge"), 5), new RunningJob(Key(_sql, "Load"), 5)],
                "prod-01").Count);
    }

    [Fact]
    public void Ordered_Jobs_FailingFirstThenRunningThenByName()
    {
        JobEntry[] entries =
        [
            Entry(Summary(_billing, "zeta", JobRunStatus.Succeeded)),
            Entry(Summary(_billing, "beta", JobRunStatus.Failed)),
            new(Key(_sql, "omega"), null, new RunningJob(Key(_sql, "omega"), 10)),
            Entry(Summary(_billing, "Alpha", JobRunStatus.Succeeded)),
            Entry(Summary(_sql, "delta", JobRunStatus.Failed)),
        ];

        var names = JobListing.Ordered(entries).Select(entry => entry.Job.Name);

        Assert.Equal(["beta", "delta", "omega", "Alpha", "zeta"], names);
    }

    [Fact]
    public void Keep_SystemJobsAndStatus_LeavesSqlServersOwnJobsOutAndKeepsWhatIsAskedFor()
    {
        JobEntry[] entries =
        [
            Entry(Summary(_sql, "syspolicy_purge_history", JobRunStatus.Failed)),
            Entry(Summary(_billing, "syspolicy_purge_history", JobRunStatus.Succeeded)),
            Entry(Summary(_billing, "import", JobRunStatus.Failed)),
            new(Key(_sql, "rebuild"), null, new RunningJob(Key(_sql, "rebuild"), 10)),
        ];

        string[] Names(bool system, JobStatusFilter status) =>
            [.. JobListing.Keep(entries, system, status).Select(entry => entry.Job.Name).Order()];

        Assert.Equal(["import", "rebuild", "syspolicy_purge_history"], Names(false, JobStatusFilter.All));
        Assert.Equal(4, JobListing.Keep(entries, true, JobStatusFilter.All).Count);
        Assert.Equal(["import"], Names(false, JobStatusFilter.Failing));
        Assert.Equal(["rebuild"], Names(false, JobStatusFilter.Running));
    }

    [Fact]
    public void Item_JobRunningNow_LastRunIsRunningAndTheStripEndsWithIt()
    {
        var summary = Summary(_sql, "Nightly backup", JobRunStatus.Failed) with
        {
            Recent = [.. Enumerable.Repeat(JobRunStatus.Succeeded, 19), JobRunStatus.Failed],
        };
        var entry = new JobEntry(summary.Job, summary, new RunningJob(summary.Job, 90));

        var item = JobListing.Item(entry, _end);

        Assert.Equal(JobIds.Encode(summary.Job), item.Id);
        Assert.Equal(("sqlagent", "database", "microsoft.sql_server SQL-PROD-01", null), (
            item.Source,
            item.Block.Kind,
            item.Block.Name,
            item.Block.Part));
        Assert.Equal("database:production/microsoft.sql_server SQL-PROD-01", item.Block.Id);
        Assert.Equal((_end.AddSeconds(-90), null, "running"), (
            item.LastRun!.StartedAt,
            item.LastRun.DurationSeconds,
            item.LastRun.Status));
        Assert.True(item.Running);
        Assert.Equal(20, item.Recent.Count);
        Assert.Equal(["failed", "running"], item.Recent.TakeLast(2));
        Assert.Equal((4, 1, 0.75, 30d), (item.Runs, item.Failed, item.SuccessRatio, item.UsualSeconds));
    }

    [Fact]
    public void Item_RunningJobWithoutRuns_HasNoRatioAndOnlyTheRunningRunInItsStrip()
    {
        var job = Key(_sql, "Index rebuild");

        var item = JobListing.Item(new JobEntry(job, null, new RunningJob(job, 5)), _end);

        Assert.Equal((0, 0, null, null), (item.Runs, item.Failed, item.SuccessRatio, item.UsualSeconds));
        Assert.Equal(["running"], item.Recent);
    }

    [Fact]
    public void Figures_Jobs_AddUpTheRunsOfEveryJob()
    {
        JobEntry[] entries =
        [
            Entry(Summary(_billing, "import", JobRunStatus.Failed)),
            Entry(Summary(_sql, "backup", JobRunStatus.Succeeded)),
            new(Key(_sql, "rebuild"), null, new RunningJob(Key(_sql, "rebuild"), 10)),
        ];

        var figures = JobListing.Figures(entries);

        Assert.Equal((3, 8, 2, 0.75, 1), (
            figures.Jobs,
            figures.Runs,
            figures.Failed,
            figures.SuccessRatio,
            figures.Running));
    }

    [Fact]
    public void Figures_NoRuns_HaveNoRatio()
    {
        Assert.Null(JobListing.Figures([]).SuccessRatio);
    }

    private static JobKey Key(TelemetryBlock block, string name) => new(
        block.Kind == AlertBlockKind.Database ? JobSource.SqlAgent : JobSource.Trace,
        "production",
        block,
        name);

    private static JobEntry Entry(JobSummary summary) => new(summary.Job, summary, null);

    // Four runs, three of which succeeded.
    private static JobSummary Summary(TelemetryBlock block, string name, JobRunStatus last) => new(
        Key(block, name),
        4,
        3,
        1,
        new JobDuration(_end.AddHours(-1), 12, last),
        [JobRunStatus.Succeeded, JobRunStatus.Succeeded, JobRunStatus.Failed, last],
        30,
        25,
        40,
        null);
}
