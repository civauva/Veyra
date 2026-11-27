namespace Veyra.Query.Overview.Api;

/// <summary>How a job's latest run stands.</summary>
public enum PipelineStatus
{
    /// <summary>It succeeded.</summary>
    Succeeded,

    /// <summary>It failed, was cancelled, or is being retried.</summary>
    Failed,

    /// <summary>It runs now.</summary>
    Running,
}
