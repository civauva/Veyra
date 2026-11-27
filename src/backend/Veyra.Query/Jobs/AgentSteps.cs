using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Jobs;

namespace Veyra.Query.Jobs;

/// <summary>
/// The steps of a SQL Server Agent run (ADR 0027). They stay in the agent's job history, kept 90 days, and are matched
/// to a run as the server's Jobs tab matches them (ADR 0020): the records of the same job, other than step 0, that ran
/// within the run's duration. Pure; tested without a database.
/// </summary>
internal static class AgentSteps
{
    /// <summary>The kind of activity record a run or step of a SQL Server Agent job is.</summary>
    public const string Kind = "sqlserver.job";

    // SQL Server Agent's run_status of a step that is being retried.
    private const string _retry = "2";

    /// <summary>A run's steps, in step order, and how many times one of them was retried.</summary>
    /// <param name="run">The run.</param>
    /// <param name="records">The job history records around the run; a record sent twice counts once.</param>
    /// <returns>
    /// The steps, and the retries: the step records SQL Server marked as retried, or null when none of its steps is
    /// in the history.
    /// </returns>
    public static (IReadOnlyList<JobStep> Steps, int? Retries) For(JobRun run, IEnumerable<ActivityRecord> records)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(records);

        var length = run.DurationSeconds ?? 0;
        var own = records
            .Where(record => record.Attributes.GetValueOrDefault("job_name") == run.Job.Name
                && record.Attributes.GetValueOrDefault("step_id") is { } step
                && step != "0"
                && Math.Abs((record.SampledAt - run.StartedAt).TotalSeconds) <= length + 1)
            .GroupBy(record => record.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(record => Number(record, "step_id"))
            .ThenBy(record => record.SampledAt)
            .ToList();

        if (own.Count == 0)
        {
            return ([], null);
        }

        var steps = own
            .Select(record => new JobStep(
                record.Attributes.GetValueOrDefault("step_name") is { Length: > 0 } name
                    ? name
                    : $"Step {record.Attributes.GetValueOrDefault("step_id")}",
                Number(record, "duration"),
                StatusOf(record.Attributes.GetValueOrDefault("run_status")),
                string.IsNullOrEmpty(record.Body) ? null : record.Body))
            .ToList();

        return (steps, own.Count(record => record.Attributes.GetValueOrDefault("run_status") == _retry));
    }

    /// <summary>A step's status from SQL Server Agent's <c>run_status</c>.</summary>
    /// <remarks>1 succeeded and 4 is in progress; 0 failed, 2 is being retried, and 3 was cancelled.</remarks>
    /// <param name="runStatus">The <c>run_status</c> column.</param>
    /// <returns>The status, or null for a value SQL Server does not document.</returns>
    public static JobRunStatus? StatusOf(string? runStatus) => runStatus switch
    {
        "1" => JobRunStatus.Succeeded,
        "0" or "2" or "3" => JobRunStatus.Failed,
        "4" => JobRunStatus.Running,
        _ => null,
    };

    private static double Number(ActivityRecord record, string key) =>
        double.TryParse(
            record.Attributes.GetValueOrDefault(key),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
}
