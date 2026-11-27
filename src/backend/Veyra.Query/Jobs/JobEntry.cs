using Veyra.Core.Jobs;

namespace Veyra.Query.Jobs;

/// <summary>A job as the Jobs list and the Overview see it: its runs of the window, and whether it runs now.</summary>
/// <param name="Job">The job.</param>
/// <param name="Summary">Its runs of the window added up; null when none started in it.</param>
/// <param name="Running">The run going on now, when the agent says so; null otherwise.</param>
internal sealed record JobEntry(JobKey Job, JobSummary? Summary, RunningJob? Running)
{
    /// <summary>Whether a run of the job goes on now.</summary>
    public bool IsRunning => Running is not null || Summary?.LastRun.Status == JobRunStatus.Running;

    /// <summary>How its latest run stands; a run going on now is its latest. Null without any run.</summary>
    public JobRunStatus? LastStatus => IsRunning ? JobRunStatus.Running : Summary?.LastRun.Status;
}
