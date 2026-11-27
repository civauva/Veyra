namespace Veyra.Agents.SqlServers;

/// <summary>Whether one module of a SQL Server's reads runs, and how often (ADR 0028).</summary>
/// <param name="Id">The module's ID, such as <c>activity</c>.</param>
/// <param name="Enabled">Whether it runs. Core always does.</param>
/// <param name="IntervalSeconds">How often it reads, one of the module's choices.</param>
public sealed record SqlServerModuleChoice(string Id, bool Enabled, int IntervalSeconds);
