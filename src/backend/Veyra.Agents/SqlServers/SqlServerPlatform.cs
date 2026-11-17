namespace Veyra.Agents.SqlServers;

/// <summary>The kind of SQL Server a monitored server is, which decides what the agent reads. Stored as its number.</summary>
public enum SqlServerPlatform
{
    /// <summary>
    /// SQL Server that its owner runs, on a machine or a virtual machine: every area can be read, including SQL Agent,
    /// <c>msdb</c>, and the <c>system_health</c> session.
    /// </summary>
    SqlServer = 1,

    /// <summary>
    /// Azure SQL Managed Instance: read like SQL Server, but Azure runs the backups, the host, and the cluster, so
    /// Veyra does not report on them.
    /// </summary>
    ManagedInstance = 2,

    /// <summary>
    /// Azure SQL Database: one database, read with a contained user. It has no SQL Agent, no <c>msdb</c>, and no
    /// <c>system_health</c> session.
    /// </summary>
    AzureSqlDatabase = 3,
}
