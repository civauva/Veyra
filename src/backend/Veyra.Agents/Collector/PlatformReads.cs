using System.Text.RegularExpressions;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Collector;

/// <summary>
/// What the agent reads on each platform (ADR 0020, VEY-57). The template in integrations/sqlserver/collector.yaml
/// reads a SQL Server; a Managed Instance leaves out what Azure runs for it, and an Azure SQL Database leaves out every
/// server-wide read and adds the database-scoped ones of integrations/sqlserver/azure-sql-database.yaml. A read is
/// named by the <c>/* veyra-agent: name */</c> comment that starts its SQL.
/// </summary>
internal static partial class PlatformReads
{
    // Azure takes a Managed Instance's backups and runs its host and cluster: msdb's backup history, the volumes,
    // log shipping, the cluster nodes, and the replicas of its own failover group would mislead.
    private static readonly HashSet<string> _managedInstanceQueries = new(StringComparer.Ordinal)
    {
        "backup age",
        "volumes",
        "recovery",
        "availability replicas",
        "availability databases",
        "log shipping",
        "cluster nodes",
    };

    // An Azure SQL Database has no SQL Server Agent, msdb, system_health, sp_configure, or server-wide views; its
    // sys.databases also lists master, which the log and per-database reads may not enter.
    private static readonly HashSet<string> _databaseReceivers = new(StringComparer.Ordinal)
    {
        "sqlserver",
        "sqlquery/events",
        "sqlquery/jobs",
        "sqlquery/config",
        "sqlquery/server",
        "sqlquery/indexes",
        "sqlquery/querystore",
    };

    private static readonly HashSet<string> _databaseQueries = new(StringComparer.Ordinal)
    {
        "database size",
        "backup age",
        "job last run",
        "permissions",
        "logs",
        "files",
        "volumes",
        "recovery",
    };

    /// <summary>How the agent names the platform in what it sends, such as <c>managed-instance</c>.</summary>
    /// <param name="platform">The platform.</param>
    /// <returns>The name.</returns>
    public static string Name(SqlServerPlatform platform) =>
        platform switch
        {
            SqlServerPlatform.ManagedInstance => "managed-instance",
            SqlServerPlatform.AzureSqlDatabase => "azure-sql-database",
            _ => "sqlserver",
        };

    /// <summary>Whether the platform reads a receiver of the template at all.</summary>
    /// <param name="platform">The platform.</param>
    /// <param name="receiver">The receiver's name in the template, such as <c>sqlquery/jobs</c>.</param>
    /// <returns>False for a receiver the platform has nothing for.</returns>
    public static bool Reads(SqlServerPlatform platform, string receiver) =>
        platform != SqlServerPlatform.AzureSqlDatabase || !_databaseReceivers.Contains(receiver);

    /// <summary>Whether the platform runs a query of the template.</summary>
    /// <param name="platform">The platform.</param>
    /// <param name="sql">The query's SQL, which starts with its <c>/* veyra-agent: name */</c> comment.</param>
    /// <returns>False for a query the platform has nothing for.</returns>
    public static bool Runs(SqlServerPlatform platform, string sql)
    {
        var name = QueryName().Match(sql) is { Success: true } match ? match.Groups["name"].Value.Trim() : "";

        return platform switch
        {
            SqlServerPlatform.ManagedInstance => !_managedInstanceQueries.Contains(name),
            SqlServerPlatform.AzureSqlDatabase => !_databaseQueries.Contains(name),
            _ => true,
        };
    }

    [GeneratedRegex(@"/\*\s*veyra-agent:(?<name>[^*]+)\*/")]
    private static partial Regex QueryName();
}
