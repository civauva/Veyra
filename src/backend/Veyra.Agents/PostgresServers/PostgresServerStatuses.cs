using Veyra.Agents.Agents;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents.PostgresServers;

/// <summary>
/// Tells a PostgreSQL server's state the way a SQL Server's is told (ADR 0019): from its agent's report, the agent's
/// own error logs, and whether the server's data arrives. Only the words of the errors differ.
/// </summary>
internal static class PostgresServerStatuses
{
    /// <summary>The <c>db.system.name</c> of PostgreSQL, which the template sets on everything it reads.</summary>
    public const string Engine = "postgresql";

    // Words in the errors of lib/pq, which both the postgresql and the sqlquery receivers use, and of Go's network and
    // TLS code, first match wins. pg_hba.conf turning the agent away is a login failure: the role cannot sign in.
    private static readonly (string Words, SqlServerState State)[] _reasons =
    [
        ("password authentication failed", SqlServerState.LoginFailed),
        ("no pg_hba.conf entry", SqlServerState.LoginFailed),
        ("permission denied", SqlServerState.GrantMissing),

        // A role that does not exist, or may not log in.
        ("pq: role ", SqlServerState.LoginFailed),
        ("x509:", SqlServerState.CertificateRejected),
        ("failed to verify certificate", SqlServerState.CertificateRejected),
        ("SSL is not enabled on the server", SqlServerState.CertificateRejected),
        ("connection refused", SqlServerState.Unreachable),
        ("no such host", SqlServerState.Unreachable),
        ("i/o timeout", SqlServerState.Unreachable),
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
        PostgresServer server,
        AgentReport agent,
        IEnumerable<ReaderError> errors,
        DateTimeOffset? lastData,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(server);

        return SqlServerStatuses.Of(server.Id, server.ChangedAt, agent, errors, lastData, now, _reasons);
    }
}
