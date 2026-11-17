using Veyra.Agents.Agents;

namespace Veyra.Agents.SqlServers;

/// <summary>
/// Tells a SQL Server's state from its agent's report, the agent's own error logs, and whether the server's data
/// arrives (ADR 0019).
/// </summary>
/// <remarks>
/// The Collector's health stays good through a wrong password or a missing grant: it logs an error on every read and
/// carries on. So the reasons come from those logs, matched to the server by the failing component's name.
/// </remarks>
internal static class SqlServerStatuses
{
    /// <summary>How long an agent may go without polling before it counts as offline: four missed polls.</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How far back an error still counts. The receivers read every 15 and 60 seconds, so a lasting fault logs again
    /// within it, and a fixed one stops counting soon after.
    /// </summary>
    public static readonly TimeSpan ErrorsWithin = TimeSpan.FromMinutes(2);

    // Errors that stop every read: while one lasts, no figure arrives. Figures newer than such an error mean it is
    // fixed, as when the login script is run after the server was added.
    private static readonly HashSet<SqlServerState> _noData =
    [
        SqlServerState.LoginFailed,
        SqlServerState.Unreachable,
        SqlServerState.CertificateRejected,
    ];

    // Words in the errors of the SQL Server driver and of Go's network and TLS code, first match wins.
    private static readonly (string Words, SqlServerState State)[] _reasons =
    [
        ("Login failed", SqlServerState.LoginFailed),
        ("permission was denied", SqlServerState.GrantMissing),
        ("x509:", SqlServerState.CertificateRejected),
        ("failed to verify certificate", SqlServerState.CertificateRejected),
        ("connection refused", SqlServerState.Unreachable),
        ("no such host", SqlServerState.Unreachable),
        ("i/o timeout", SqlServerState.Unreachable),
        ("unable to open tcp connection", SqlServerState.Unreachable),
        ("network is unreachable", SqlServerState.Unreachable),
    ];

    /// <summary>Tells one server's state.</summary>
    /// <param name="server">The server.</param>
    /// <param name="agent">What its agent last reported.</param>
    /// <param name="errors">Recent reader errors of the agent's servers; other servers' errors are ignored.</param>
    /// <param name="lastData">When the server's figures last arrived, or <see langword="null"/> for not lately.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The state, with the error or time behind it.</returns>
    public static SqlServerStatus Of(
        SqlServer server,
        AgentReport agent,
        IEnumerable<ReaderError> errors,
        DateTimeOffset? lastData,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(server);

        return Of(server.Id, server.ChangedAt, agent, errors, lastData, now, _reasons);
    }

    /// <summary>
    /// Tells the state of any server an agent reads, SQL Server or PostgreSQL (VEY-33), from the words its errors are
    /// matched against.
    /// </summary>
    /// <param name="serverId">The server's ID, which its Collector components are named after.</param>
    /// <param name="changedAt">When the server was last changed; older errors belong to its old settings.</param>
    /// <param name="agent">What its agent last reported.</param>
    /// <param name="errors">Recent reader errors of the agent's servers; other servers' errors are ignored.</param>
    /// <param name="lastData">When the server's figures last arrived, or <see langword="null"/> for not lately.</param>
    /// <param name="now">The current time.</param>
    /// <param name="reasons">Words in the engine's errors and the state each means, first match wins.</param>
    /// <returns>The state, with the error or time behind it.</returns>
    public static SqlServerStatus Of(
        Guid serverId,
        DateTimeOffset changedAt,
        AgentReport agent,
        IEnumerable<ReaderError> errors,
        DateTimeOffset? lastData,
        DateTimeOffset now,
        IReadOnlyList<(string Words, SqlServerState State)> reasons)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (agent.LastSeenAt is not { } seen)
        {
            return new SqlServerStatus(SqlServerState.AgentNotConnected, null, null);
        }

        if (now - seen > OfflineAfter)
        {
            return new SqlServerStatus(SqlServerState.AgentOffline, null, seen);
        }

        if (agent.ConfigState == AgentConfigState.Failed)
        {
            return new SqlServerStatus(SqlServerState.ConfigurationFailed, agent.ConfigError, null);
        }

        // An error from before the server was last changed belongs to the old settings.
        var latest = errors
            .Where(error => error.ServerId == serverId
                && error.At > changedAt
                && now - error.At <= ErrorsWithin)
            .MaxBy(error => error.At);

        if (latest is not null)
        {
            var state = reasons
                .Where(reason => latest.Message.Contains(reason.Words, StringComparison.OrdinalIgnoreCase))
                .Select(reason => (SqlServerState?)reason.State)
                .FirstOrDefault() ?? SqlServerState.Failing;

            if (!(_noData.Contains(state) && lastData > latest.At))
            {
                return new SqlServerStatus(state, latest.Message, latest.At);
            }
        }

        return lastData is { } arrived
            ? new SqlServerStatus(SqlServerState.Receiving, null, arrived)
            : new SqlServerStatus(SqlServerState.WaitingForData, null, null);
    }

    /// <summary>Finds the server a Collector component reads, from the component's name.</summary>
    /// <param name="componentId">The component's ID, such as <c>sqlserver/0195a0…</c>.</param>
    /// <returns>The server's ID, or <see langword="null"/> when the component reads no server.</returns>
    public static Guid? ServerOf(string componentId)
    {
        // The server's ID ends the name, after the last slash: sqlserver/0195…, or sqlquery/live/0195… (ADR 0020).
        var slash = componentId.LastIndexOf('/');

        return slash > 0 && Guid.TryParseExact(componentId[(slash + 1)..], "N", out var id) ? id : null;
    }
}
