using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Api;

/// <summary>A SQL Server as the Database monitoring page shows it. Its password is never sent back.</summary>
/// <param name="Id">The server's ID.</param>
/// <param name="Name">Its name in Veyra, its database block.</param>
/// <param name="Address">The host name or IP address the agent connects to.</param>
/// <param name="Port">The TCP port.</param>
/// <param name="Environment">The environment its telemetry is filed under.</param>
/// <param name="AgentId">The agent that reads it.</param>
/// <param name="AgentName">That agent's name.</param>
/// <param name="Login">The SQL login the agent signs in with.</param>
/// <param name="Encryption">How the connection is encrypted.</param>
/// <param name="State">How its monitoring stands.</param>
/// <param name="Detail">The error behind the state, as the agent or SQL Server gave it, when there is one.</param>
/// <param name="Since">When that error or the last data was seen, when known.</param>
/// <param name="CreatedAt">When it was added.</param>
/// <param name="ChangedAt">When it was last changed.</param>
public sealed record SqlServerResponse(
    Guid Id,
    string Name,
    string Address,
    int Port,
    string Environment,
    Guid AgentId,
    string AgentName,
    string Login,
    SqlServerEncryption Encryption,
    SqlServerState State,
    string? Detail,
    DateTimeOffset? Since,
    DateTimeOffset CreatedAt,
    DateTimeOffset ChangedAt);
