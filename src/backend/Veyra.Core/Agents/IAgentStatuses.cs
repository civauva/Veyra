namespace Veyra.Core.Agents;

/// <summary>
/// Lists Veyra's agents with their states. The Agents module implements it; the Query module shows the Overview's
/// collector health from it (ADR 0023).
/// </summary>
public interface IAgentStatuses
{
    /// <summary>Lists the active agents, the built-in one first.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The agents.</returns>
    Task<IReadOnlyList<AgentStatus>> ListAsync(CancellationToken cancellationToken);
}
