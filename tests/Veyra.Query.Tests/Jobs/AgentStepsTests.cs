using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;
using Veyra.Query.Jobs;

namespace Veyra.Query.Tests.Jobs;

/// <summary>
/// A SQL Server Agent run's steps stay in its job history (ADR 0027) and are matched to the run by time, as the
/// server's Jobs tab matches them (ADR 0020).
/// </summary>
public class AgentStepsTests
{
    private static readonly DateTimeOffset _start = new(2026, 11, 26, 2, 0, 0, TimeSpan.Zero);

    private static readonly JobKey _job = new(
        JobSource.SqlAgent,
        null,
        TelemetryBlock.Database("microsoft.sql_server", "SQL-PROD-01"),
        "Nightly ETL");

    [Fact]
    public void For_RunAndItsHistory_ListsItsStepsInOrderWithTheirRetries()
    {
        var run = Run(_start, 120);
        ActivityRecord[] records =
        [
            Record("1", "0", "0", 120, _start, "The job failed."),
            Record("4", "2", "0", 60, _start.AddSeconds(50), "Login failed for user 'etl'."),
            Record("3", "2", "2", 30, _start.AddSeconds(20), "Retrying."),
            Record("2", "1", "1", 20, _start, "Done."),
            Record("2", "1", "1", 20, _start, "Done."),
            Record("9", "1", "1", 20, _start.AddDays(-1), "Yesterday."),
            Record("8", "1", "4", 0, _start.AddSeconds(60), "Going on.") with
            {
                Attributes = new Dictionary<string, string>
                {
                    ["job_name"] = "Other job",
                    ["step_id"] = "1",
                    ["run_status"] = "1",
                },
            },
        ];

        var (steps, retries) = AgentSteps.For(run, records);

        Assert.Equal(
            [
                new JobStep("Step 1", 20, JobRunStatus.Succeeded, "Done."),
                new JobStep("Step 2", 30, JobRunStatus.Failed, "Retrying."),
                new JobStep("Step 2", 60, JobRunStatus.Failed, "Login failed for user 'etl'."),
            ],
            steps);
        Assert.Equal(1, retries);
    }

    [Fact]
    public void For_RunWithoutStepsInTheHistory_HasNoStepsAndNoRetryCount()
    {
        var (steps, retries) = AgentSteps.For(Run(_start, 10), [Record("1", "0", "1", 10, _start, "")]);

        Assert.Empty(steps);
        Assert.Null(retries);
    }

    [Theory]
    [InlineData("1", JobRunStatus.Succeeded)]
    [InlineData("0", JobRunStatus.Failed)]
    [InlineData("2", JobRunStatus.Failed)]
    [InlineData("3", JobRunStatus.Failed)]
    [InlineData("4", JobRunStatus.Running)]
    [InlineData("7", null)]
    public void StatusOf_RunStatus_IsTheStepsStatus(string runStatus, JobRunStatus? expected)
    {
        Assert.Equal(expected, AgentSteps.StatusOf(runStatus));
    }

    private static JobRun Run(DateTimeOffset start, double seconds) =>
        new(_job, "1", start, seconds, JobRunStatus.Failed, [], null, null, null, null, null);

    private static ActivityRecord Record(
        string key,
        string step,
        string status,
        int seconds,
        DateTimeOffset at,
        string message) => new(
        _job.Block.Name,
        null,
        at,
        "sqlserver.job",
        key,
        new Dictionary<string, string>
        {
            ["job_name"] = _job.Name,
            ["step_id"] = step,
            ["step_name"] = step == "0" ? "(Job outcome)" : $"Step {step}",
            ["run_status"] = status,
            ["duration"] = seconds.ToString(CultureInfo.InvariantCulture),
        },
        message);
}
