namespace Veyra.Agents.PostgresServers;

/// <summary>
/// A PostgreSQL server that a Veyra agent reads, as set up on the Settings screen (VEY-33). Its password is kept
/// apart, encrypted.
/// </summary>
/// <param name="Id">The server's ID. Its agent's Collector components are named after it.</param>
/// <param name="Name">
/// The server's name in Veyra, its database block (ADR 0018), such as <c>pg-main</c>. Unique among PostgreSQL servers.
/// </param>
/// <param name="Address">The host name or IP address the agent connects to.</param>
/// <param name="Port">The TCP port, 5432 unless the server listens elsewhere.</param>
/// <param name="Environment">The environment its telemetry is filed under, such as <c>production</c>.</param>
/// <param name="AgentId">The agent that reads it.</param>
/// <param name="Login">The role the agent signs in as, a member of <c>pg_monitor</c>.</param>
/// <param name="Encryption">How the connection is encrypted.</param>
/// <param name="CreatedAt">When it was added.</param>
/// <param name="ChangedAt">When it was last changed.</param>
internal sealed record PostgresServer(
    Guid Id,
    string Name,
    string Address,
    int Port,
    string Environment,
    Guid AgentId,
    string Login,
    PostgresEncryption Encryption,
    DateTimeOffset CreatedAt,
    DateTimeOffset ChangedAt);
