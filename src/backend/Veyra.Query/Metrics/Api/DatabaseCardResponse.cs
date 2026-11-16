namespace Veyra.Query.Metrics.Api;

/// <summary>A database server on the Metrics overview, and the head of its screen (ADR 0018).</summary>
/// <param name="Engine">The engine, as <c>db.system.name</c> spells it, such as <c>microsoft.sql_server</c>.</param>
/// <param name="Server">The server, as <c>server.address</c> names it, such as <c>sql-prod-01</c>.</param>
/// <param name="Environment">The environment, or null when the server sent none.</param>
/// <param name="LastSeen">When the server last sent a point.</param>
/// <param name="Cpus">The logical CPUs SQL Server sees.</param>
/// <param name="MemoryBytes">The memory SQL Server uses.</param>
/// <param name="UptimeSeconds">How long the machine has been up.</param>
/// <param name="BatchRequests">Batch requests per second now.</param>
/// <param name="Connections">User connections now.</param>
/// <param name="Blocked">Sessions blocked now.</param>
/// <param name="Deadlocks">Deadlocks in the last 15 minutes.</param>
/// <param name="PageLifeSeconds">Page life expectancy now.</param>
/// <param name="LongestQuerySeconds">The longest running query now, of any database.</param>
/// <param name="Databases">How many databases the server has.</param>
/// <param name="OldestBackupSeconds">The longest any database has gone without a full backup.</param>
/// <param name="FailedJobs">SQL Agent jobs whose last run failed.</param>
/// <param name="Requests">Batch requests per second over the window.</param>
public sealed record DatabaseCardResponse(
    string Engine,
    string Server,
    string? Environment,
    DateTimeOffset LastSeen,
    int? Cpus,
    double? MemoryBytes,
    double? UptimeSeconds,
    double? BatchRequests,
    double? Connections,
    double? Blocked,
    double? Deadlocks,
    double? PageLifeSeconds,
    double? LongestQuerySeconds,
    int Databases,
    double? OldestBackupSeconds,
    int FailedJobs,
    ChartLineResponse Requests);
