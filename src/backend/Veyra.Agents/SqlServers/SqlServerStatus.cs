namespace Veyra.Agents.SqlServers;

/// <summary>A SQL Server's state, with what tells it.</summary>
/// <param name="State">The state.</param>
/// <param name="Detail">
/// The error behind it, as the agent or SQL Server gave it, for the states that come from one; otherwise
/// <see langword="null"/>.
/// </param>
/// <param name="Since">When the error or the last data was seen, when known.</param>
public sealed record SqlServerStatus(SqlServerState State, string? Detail, DateTimeOffset? Since);
