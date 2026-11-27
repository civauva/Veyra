namespace Veyra.Query.Overview.Api;

/// <summary>One database of the Overview's list across servers.</summary>
/// <param name="Name">The database.</param>
/// <param name="Server">Its server, as <c>server.address</c> names it.</param>
/// <param name="Engine">The engine, as <c>db.system.name</c> spells it.</param>
/// <param name="State">
/// Its state, such as <c>Online</c> or <c>Recovery pending</c>, from the agent's storage read of SQL Server; null
/// where the engine or the agent does not send it.
/// </param>
/// <param name="SizeBytes">Its data and log files at the end of the window.</param>
/// <param name="GrowthBytesPerDay">How much it grew per day at the pace of the last day.</param>
/// <param name="LastFullBackupAgeSeconds">
/// Time since its last full backup; null where it never had one or the engine does not send it.
/// </param>
/// <param name="Late">Whether it has no full backup within 26 hours (SQL Server, tempdb aside).</param>
/// <param name="Connections">Connections to it now, where the engine reports them per database (PostgreSQL).</param>
public sealed record OverviewDatabaseResponse(
    string Name,
    string Server,
    string Engine,
    string? State,
    double? SizeBytes,
    double? GrowthBytesPerDay,
    double? LastFullBackupAgeSeconds,
    bool Late,
    double? Connections);
