namespace Veyra.Core.Metrics.Health;

/// <summary>A SQL Agent job, as its rule judges it.</summary>
/// <param name="Name">The job name.</param>
/// <param name="Failed">Whether its last run failed.</param>
public sealed record AgentJobHealth(string Name, bool Failed);
