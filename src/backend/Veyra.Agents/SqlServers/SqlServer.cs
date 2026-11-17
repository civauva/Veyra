namespace Veyra.Agents.SqlServers;

/// <summary>
/// A SQL Server that a Veyra agent reads, as set up on the Settings screen. Its password is kept apart, encrypted.
/// </summary>
/// <param name="Id">The server's ID. Its agent's Collector components are named after it.</param>
/// <param name="Name">
/// The server's name in Veyra, its database block (ADR 0018), such as <c>sql-prod-01</c>. Unique.
/// </param>
/// <param name="Address">The host name or IP address the agent connects to.</param>
/// <param name="Port">The TCP port, 1433 unless the server listens elsewhere.</param>
/// <param name="Environment">The environment its telemetry is filed under, such as <c>production</c>.</param>
/// <param name="AgentId">The agent that reads it.</param>
/// <param name="Login">The SQL login the agent signs in with.</param>
/// <param name="Encryption">How the connection is encrypted.</param>
/// <param name="CreatedAt">When it was added.</param>
/// <param name="ChangedAt">When it was last changed.</param>
/// <param name="Platform">The kind of SQL Server it is.</param>
/// <param name="Database">
/// The one database it is read in, for an Azure SQL Database; otherwise <see langword="null"/>.
/// </param>
internal sealed record SqlServer(
    Guid Id,
    string Name,
    string Address,
    int Port,
    string Environment,
    Guid AgentId,
    string Login,
    SqlServerEncryption Encryption,
    DateTimeOffset CreatedAt,
    DateTimeOffset ChangedAt,
    SqlServerPlatform Platform = SqlServerPlatform.SqlServer,
    string? Database = null);
