namespace Veyra.Query.Metrics.Api;

/// <summary>The last run of a SQL Agent job.</summary>
/// <param name="Name">The job name.</param>
/// <param name="Failed">Whether the last run failed.</param>
/// <param name="LastRun">When the last run started, or null when it was not sent.</param>
/// <param name="DurationSeconds">How long the last run took.</param>
public sealed record AgentJobResponse(string Name, bool Failed, DateTimeOffset? LastRun, double? DurationSeconds)
{
    /// <summary>Whether SQL Server set it up for a feature of its own (VEY-64), such as policy management.</summary>
    public bool System { get; init; }
}
