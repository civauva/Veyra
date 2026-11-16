namespace Veyra.Agents.SqlServers;

/// <summary>
/// How a SQL Server's monitoring stands, as the Settings screen says it in words. The first that applies wins, in the
/// order listed.
/// </summary>
public enum SqlServerState
{
    /// <summary>Its agent has never connected to Veyra.</summary>
    AgentNotConnected,

    /// <summary>Its agent has not polled for over two minutes.</summary>
    AgentOffline,

    /// <summary>The agent's Collector refused the configuration Veyra sent; it runs with the one before.</summary>
    ConfigurationFailed,

    /// <summary>SQL Server refused the login: a wrong login name or password.</summary>
    LoginFailed,

    /// <summary>The agent cannot reach the server: a wrong address or port, or a firewall.</summary>
    Unreachable,

    /// <summary>The agent does not trust the server's certificate.</summary>
    CertificateRejected,

    /// <summary>The login works but lacks a grant from the login script, so some figures are missing.</summary>
    GrantMissing,

    /// <summary>Reading fails for another reason, given with it.</summary>
    Failing,

    /// <summary>The server's figures arrive.</summary>
    Receiving,

    /// <summary>Nothing has failed and nothing has arrived yet, as in the first minute after adding a server.</summary>
    WaitingForData,
}
