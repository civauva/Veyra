using Veyra.Agents.Agents;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Api;

/// <summary>
/// How many monitored servers and agents there are and how they stand, for the Settings list's lines (VEY-68), so the
/// browser need not read and count the lists.
/// </summary>
/// <param name="SqlServers">The SQL Servers agents read.</param>
/// <param name="PostgresServers">The PostgreSQL servers agents read.</param>
/// <param name="NeedLook">
/// The servers whose monitoring fails: an offline agent, refused settings, the login, the network, or a grant.
/// </param>
/// <param name="Agents">The active agents.</param>
/// <param name="AgentsOnline">Those online now.</param>
public sealed record AgentsSummaryResponse(
    int SqlServers,
    int PostgresServers,
    int NeedLook,
    int Agents,
    int AgentsOnline)
{
    /// <summary>The server states whose monitoring fails and needs a look, as the Settings screen marks them.</summary>
    public static readonly IReadOnlySet<SqlServerState> Failing = new HashSet<SqlServerState>
    {
        SqlServerState.AgentOffline,
        SqlServerState.ConfigurationFailed,
        SqlServerState.LoginFailed,
        SqlServerState.Unreachable,
        SqlServerState.CertificateRejected,
        SqlServerState.GrantMissing,
        SqlServerState.Failing,
    };

    /// <summary>Counts the servers and agents. Pure.</summary>
    /// <param name="sqlServers">Each SQL Server's state.</param>
    /// <param name="postgresServers">Each PostgreSQL server's state.</param>
    /// <param name="agents">Each agent's state.</param>
    /// <returns>The summary.</returns>
    public static AgentsSummaryResponse Of(
        IReadOnlyList<SqlServerState> sqlServers,
        IReadOnlyList<SqlServerState> postgresServers,
        IReadOnlyList<AgentState> agents)
    {
        ArgumentNullException.ThrowIfNull(sqlServers);
        ArgumentNullException.ThrowIfNull(postgresServers);
        ArgumentNullException.ThrowIfNull(agents);

        return new AgentsSummaryResponse(
            sqlServers.Count,
            postgresServers.Count,
            sqlServers.Concat(postgresServers).Count(Failing.Contains),
            agents.Count,
            agents.Count(state => state == AgentState.Online));
    }
}
