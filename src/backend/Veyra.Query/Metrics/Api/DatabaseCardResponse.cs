namespace Veyra.Query.Metrics.Api;

/// <summary>A database server on the Metrics overview, and the head of its screen (ADR 0018).</summary>
/// <param name="Engine">The engine, as <c>db.system.name</c> spells it, such as <c>microsoft.sql_server</c>.</param>
/// <param name="Server">The server, as <c>server.address</c> names it, such as <c>sql-prod-01</c>.</param>
/// <param name="Environment">The environment, or null when the server sent none.</param>
/// <param name="LastSeen">When the server last sent a point.</param>
/// <param name="Cpus">The logical CPUs SQL Server sees.</param>
/// <param name="MemoryBytes">The memory SQL Server uses.</param>
/// <param name="UptimeSeconds">How long the machine has been up.</param>
/// <param name="BatchRequests">Batch requests per second now (SQL Server).</param>
/// <param name="Connections">User connections now; for PostgreSQL, the connections to every database.</param>
/// <param name="Blocked">Sessions blocked now.</param>
/// <param name="Deadlocks">Deadlocks in the last 15 minutes.</param>
/// <param name="PageLifeSeconds">Page life expectancy now.</param>
/// <param name="LongestQuerySeconds">The longest running query now, of any database.</param>
/// <param name="Databases">How many databases the server has.</param>
/// <param name="OldestBackupSeconds">The longest any database has gone without a full backup.</param>
/// <param name="FailedJobs">SQL Agent jobs whose last run failed.</param>
/// <param name="Requests">
/// Batch requests per second over the window; for PostgreSQL, transactions committed or rolled back per second.
/// </param>
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
    ChartLineResponse Requests)
{
    /// <summary>The server's maximum connections; null where the engine does not say it (PostgreSQL does).</summary>
    public double? MaxConnections { get; init; }

    /// <summary>Committed transactions per second now, of every database (PostgreSQL).</summary>
    public double? CommitsPerSecond { get; init; }

    /// <summary>Rolled back transactions per second now, of every database (PostgreSQL).</summary>
    public double? RollbacksPerSecond { get; init; }

    /// <summary>
    /// The highest transaction ID age of any database, of the wraparound limit, from 0 to 1 (PostgreSQL).
    /// </summary>
    public double? WraparoundRatio { get; init; }
}
