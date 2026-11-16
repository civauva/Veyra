using Veyra.Agents.Agents;
using Veyra.Agents.Enrollment;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents;

/// <summary>
/// What the Settings screen's Agents and Database monitoring pages show: each agent and each server, with its state
/// in words.
/// </summary>
/// <param name="agents">Where agents are stored.</param>
/// <param name="servers">The SQL Servers agents read.</param>
/// <param name="signals">The agents' own errors and the servers' latest figures.</param>
/// <param name="codes">The open one-time codes of agents waiting to be installed.</param>
/// <param name="clock">The current time, for what counts as recent.</param>
internal sealed class AgentsOverview(
    AgentStore agents,
    SqlServerService servers,
    SqlServerSignals signals,
    EnrollmentStore codes,
    TimeProvider clock)
{
    /// <summary>Lists the active agents with their states and how many servers each reads.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The agents, the built-in one first.</returns>
    public async Task<IReadOnlyList<AgentView>> ListAgentsAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var all = await agents.ListAsync(cancellationToken);
        var counts = (await servers.ListAsync(cancellationToken))
            .GroupBy(server => server.AgentId)
            .ToDictionary(group => group.Key, group => group.Count());
        var open = await codes.OpenCodesAsync(now, cancellationToken);

        return all
            .Select(agent => new AgentView(
                agent,
                StateOf(agent.Report, now),
                counts.GetValueOrDefault(agent.Id),
                open.TryGetValue(agent.Id, out var expiresAt) ? expiresAt : null))
            .ToList();
    }

    /// <summary>Lists every SQL Server with its agent's name and its state.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The servers, by name.</returns>
    public async Task<IReadOnlyList<SqlServerView>> ListSqlServersAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var list = await servers.ListAsync(cancellationToken);

        if (list.Count == 0)
        {
            return [];
        }

        var byId = (await agents.ListAsync(cancellationToken)).ToDictionary(agent => agent.Id);
        var errors = await signals.ReadErrorsAsync(now, cancellationToken);
        var views = new List<SqlServerView>(list.Count);

        foreach (var server in list)
        {
            var agent = byId.GetValueOrDefault(server.AgentId);
            var lastData = await signals.LastDataAsync(server, now, cancellationToken);
            var status = SqlServerStatuses.Of(server, agent?.Report ?? AgentReport.None, errors, lastData, now);

            views.Add(new SqlServerView(server, agent?.Name ?? "", status));
        }

        return views;
    }

    /// <summary>Tells an agent's state from its last report.</summary>
    /// <param name="report">The agent's report.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The state.</returns>
    internal static AgentState StateOf(AgentReport report, DateTimeOffset now) =>
        report switch
        {
            { LastSeenAt: null } => AgentState.NotConnected,
            { LastSeenAt: { } seen } when now - seen > SqlServerStatuses.OfflineAfter => AgentState.Offline,
            { ConfigState: AgentConfigState.Failed } => AgentState.ConfigurationFailed,
            { Healthy: false } => AgentState.Unhealthy,
            _ => AgentState.Online,
        };
}

/// <summary>An agent with its state.</summary>
/// <param name="Agent">The agent.</param>
/// <param name="State">Its state.</param>
/// <param name="Servers">How many servers it reads.</param>
/// <param name="CodeExpiresAt">When its open one-time code stops working; <see langword="null"/> for none.</param>
internal sealed record AgentView(Agent Agent, AgentState State, int Servers, DateTimeOffset? CodeExpiresAt);

/// <summary>A SQL Server with its agent's name and its state.</summary>
/// <param name="Server">The server.</param>
/// <param name="AgentName">The name of the agent that reads it.</param>
/// <param name="Status">Its state.</param>
internal sealed record SqlServerView(SqlServer Server, string AgentName, SqlServerStatus Status);
