using Veyra.Core.Agents;

namespace Veyra.Agents.Agents;

/// <summary>
/// Offers each agent's state to other modules (ADR 0023), told as the Agents page tells it, so the Overview's
/// collector health and the Agents page never disagree.
/// </summary>
/// <param name="agents">Where agents are stored.</param>
/// <param name="clock">The current time, for when an agent counts as offline.</param>
internal sealed class AgentStatuses(AgentStore agents, TimeProvider clock) : IAgentStatuses
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentStatus>> ListAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        return (await agents.ListAsync(cancellationToken))
            .Select(agent => new AgentStatus(
                agent.Name,
                AgentsOverview.StateOf(agent.Report, now).ToString(),
                agent.Report.LastSeenAt,
                agent.Report.Version))
            .ToList();
    }
}
