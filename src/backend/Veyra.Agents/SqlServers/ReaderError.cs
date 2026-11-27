namespace Veyra.Agents.SqlServers;

/// <summary>An error an agent's Collector logged while reading one SQL Server.</summary>
/// <param name="ServerId">The server, told by the name of the Collector component that failed.</param>
/// <param name="Message">The error as the Collector gave it.</param>
/// <param name="At">When it was logged.</param>
/// <param name="Component">The failing component, such as <c>sqlquery/storage/0195…</c>, when known.</param>
internal sealed record ReaderError(Guid ServerId, string Message, DateTimeOffset At, string? Component = null);
