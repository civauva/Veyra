using System.Text.RegularExpressions;

namespace Veyra.Query.Systems;

/// <summary>
/// What a database server does for itself rather than for its applications (VEY-64): its own databases, statements
/// that only read or tidy its catalog, the jobs it sets up, and the waits of its background tasks. The server's screen
/// hides them unless asked, so most of what it lists is the applications' work; its alerts still watch them.
/// </summary>
/// <remarks>
/// A statement is the server's own when it runs in a system database, or when it touches no table of the application
/// and reads the catalog, a DMV, a system table, or a server property. That covers SSMS's Object Explorer, change
/// tracking's and Query Store's cleanup, and monitoring tools other than Veyra's agent, which is told apart already.
/// It reads the text, not a parse tree, so an unusual statement can fall on the wrong side; it then only moves between
/// the two lists, and nothing is lost.
/// </remarks>
internal static partial class SystemObjects
{
    private static readonly HashSet<string> _sqlServerDatabases =
        new(["master", "model", "msdb", "tempdb", "mssqlsystemresource", "rdsadmin"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _postgresDatabases =
        new(
            ["postgres", "template0", "template1", "rdsadmin", "azure_maintenance", "azure_sys", "cloudsqladmin"],
            StringComparer.OrdinalIgnoreCase);

    // Tables of a user database that SQL Server keeps for itself, named without a schema.
    private static readonly HashSet<string> _systemTables =
        new(["MSchange_tracking_history", "syscommittab", "sysdiagrams"], StringComparer.OrdinalIgnoreCase);

    // Names that follow FROM or UPDATE without being a table.
    private static readonly HashSet<string> _keywords =
        new(
            ["SET", "SELECT", "VALUES", "TOP", "DEFAULT", "OUTPUT", "WHERE", "AS", "INTO", "FROM"],
            StringComparer.OrdinalIgnoreCase);

    // Jobs SQL Server creates for its own features: policy management, SSIS, the utility control point, the management
    // data warehouse, replication, and database mirroring.
    private static readonly string[] _jobPrefixes =
    [
        "syspolicy_", "SSIS Server Maintenance Job", "sysutility_", "collection_set_", "mdw_purge_data",
        "Agent history clean up:", "Distribution clean up:", "Expired subscription clean up",
        "Reinitialize subscriptions having data validation failures", "Replication agents checkup",
        "Replication monitoring refresher for", "Database Mirroring Monitor Job",
    ];

    // Waits of sessions with nothing to do: background tasks sleeping, queues waiting for work, WAITFOR. They grow all
    // the time on an idle server and say nothing about its load.
    private static readonly string[] _idleWaitPrefixes =
    [
        "SLEEP_", "WAITFOR", "BROKER_", "XE_", "SQLTRACE_", "FT_IFTS", "HADR_", "PWAIT_", "QDS_",
        "SP_SERVER_DIAGNOSTICS",
        "LAZYWRITER_", "CHECKPOINT_", "DIRTY_PAGE_POLL", "REQUEST_FOR_DEADLOCK_SEARCH", "LOGMGR_QUEUE",
        "ONDEMAND_TASK_QUEUE", "CLR_AUTO_EVENT", "CLR_MANUAL_EVENT", "DISPATCHER_QUEUE_SEMAPHORE", "STARTUP_",
        "AZURE_", "KSOURCE_WAKEUP", "RESOURCE_QUEUE", "SOS_WORK_DISPATCHER", "MEMORY_ALLOCATION_EXT",
    ];

    private enum Owner
    {
        Neither,
        Server,
        Application,
    }

    /// <summary>Whether a database is one the engine keeps for itself.</summary>
    /// <param name="engine">The engine, such as <c>microsoft.sql_server</c> or <c>postgresql</c>.</param>
    /// <param name="name">The database, or <see langword="null"/>.</param>
    /// <returns>True for SQL Server's master, model, msdb, tempdb, or PostgreSQL's postgres and templates.</returns>
    public static bool IsSystemDatabase(string engine, string? name)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (name is null)
        {
            return false;
        }

        return engine.Contains("postgres", StringComparison.OrdinalIgnoreCase)
            ? _postgresDatabases.Contains(name)
            : _sqlServerDatabases.Contains(name);
    }

    /// <summary>Whether a SQL Server statement is the server's own housekeeping rather than an application's.</summary>
    /// <param name="database">The database it ran in, or <see langword="null"/>.</param>
    /// <param name="text">The statement.</param>
    /// <returns>True when it ran in a system database, or reads the catalog and no table of an application.</returns>
    public static bool IsSystemStatement(string? database, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (IsSystemDatabase("microsoft.sql_server", database))
        {
            return true;
        }

        var plain = Literals().Replace(Comments().Replace(text, " "), "''");
        var common = CommonTables().Matches(plain)
            .Select(match => Unquote(match.Groups["name"].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var owners = References().Matches(plain)
            .Select(match => OwnerOf(match.Groups["name"].Value, match.Groups["call"].Success, common))
            .ToList();

        if (owners.Contains(Owner.Application))
        {
            return false;
        }

        return owners.Contains(Owner.Server) || ServerMarkers().IsMatch(plain);
    }

    /// <summary>Whether a SQL Server Agent job is one SQL Server set up for a feature of its own.</summary>
    /// <param name="name">The job's name.</param>
    /// <returns>True for jobs such as <c>syspolicy_purge_history</c>.</returns>
    public static bool IsSystemJob(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _jobPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a wait type is a background task's idle waiting, which says nothing about the load.</summary>
    /// <param name="type">The wait type, such as <c>XE_TIMER_EVENT</c>, or <see langword="null"/>.</param>
    /// <returns>True for idle waits.</returns>
    public static bool IsIdleWait(string? type) =>
        type is not null
        && _idleWaitPrefixes.Any(prefix => type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    // Whose a referenced object is: a temporary table, a table variable, a common table expression, or a built-in
    // function is neither's.
    private static Owner OwnerOf(string reference, bool call, HashSet<string> common)
    {
        var parts = reference.Split('.').Select(part => Unquote(part.Trim())).ToArray();
        var name = parts[^1];

        if (name.Length == 0 || name[0] is '#' or '@' || _keywords.Contains(name))
        {
            return Owner.Neither;
        }

        if (parts.Length == 1)
        {
            if (common.Contains(name))
            {
                return Owner.Neither;
            }

            if (_systemTables.Contains(name)
                || name.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("xp_", StringComparison.OrdinalIgnoreCase))
            {
                return Owner.Server;
            }

            return call ? Owner.Neither : Owner.Application;
        }

        var schema = parts[^2];
        var database = parts.Length > 2 ? parts[^3] : null;

        return schema.Equals("sys", StringComparison.OrdinalIgnoreCase)
            || schema.Equals("INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase)
            || IsSystemDatabase("microsoft.sql_server", database)
                ? Owner.Server
                : Owner.Application;
    }

    private static string Unquote(string part) => part.Trim('[', ']', '"');

    [GeneratedRegex(@"/\*.*?\*/|--[^\n]*", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"N?'(?:[^']|'')*'")]
    private static partial Regex Literals();

    // The tables a statement reads or writes, and the procedures it runs, with whether a parenthesis follows.
    [GeneratedRegex(
        @"\b(?:FROM|JOIN|APPLY|INTO|UPDATE|MERGE|INSERT|EXEC(?:UTE)?|DELETE(?:\s+TOP\s*\([^)]*\))?(?:\s+FROM)?)\s+"
        + @"(?<name>(?:\[[^\]]+\]|""[^""]+""|[#@\w]+)(?:\s*\.\s*(?:\[[^\]]+\]|""[^""]+""|\w+))*)(?<call>\s*\()?",
        RegexOptions.IgnoreCase)]
    private static partial Regex References();

    // The names of common table expressions, which a statement then reads as tables.
    [GeneratedRegex(@"(?:\bWITH\b|,)\s*(?<name>\[[^\]]+\]|\w+)\s*(?:\([^)]*\))?\s*\bAS\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex CommonTables();

    // What only the server's own tools ask for: server properties, @@ functions, and SMO's variables.
    [GeneratedRegex(@"@@\w+|\bserverproperty\s*\(|\bdatabasepropertyex\s*\(|@Smo\w*", RegexOptions.IgnoreCase)]
    private static partial Regex ServerMarkers();
}
