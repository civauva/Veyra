using Veyra.Agents.SqlServers;

namespace Veyra.Agents.PostgresServers;

/// <summary>A PostgreSQL server with its agent's name and its state.</summary>
/// <param name="Server">The server.</param>
/// <param name="AgentName">The name of the agent that reads it.</param>
/// <param name="Status">Its state, told in the same words as a SQL Server's.</param>
internal sealed record PostgresServerView(PostgresServer Server, string AgentName, SqlServerStatus Status);
