namespace Veyra.Core.Jobs;

/// <summary>How a job run, or one of its steps, stands (ADR 0027).</summary>
public enum JobRunStatus
{
    /// <summary>It finished without an error.</summary>
    Succeeded,

    /// <summary>It finished with an error, was cancelled, or was retried.</summary>
    Failed,

    /// <summary>It has not finished yet.</summary>
    Running,
}
