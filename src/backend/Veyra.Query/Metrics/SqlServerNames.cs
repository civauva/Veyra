namespace Veyra.Query.Metrics;

/// <summary>
/// The metric, attribute, and event names a SQL Server sends through the tested Collector configuration: the
/// <c>sqlserver</c> receiver's own, and those <c>sqlquery</c> adds under Veyra's contract (ADR 0018).
/// </summary>
internal static class SqlServerNames
{
    /// <summary>The engine, as <c>db.system.name</c> spells it.</summary>
    public const string Engine = "microsoft.sql_server";

    /// <summary>Batch requests per second, a gauge.</summary>
    public const string BatchRequests = "sqlserver.batch.request.rate";

    /// <summary>User connections, a gauge.</summary>
    public const string Connections = "sqlserver.user.connection.count";

    /// <summary>Processes blocked now, a gauge.</summary>
    public const string Blocked = "sqlserver.processes.blocked";

    /// <summary>Deadlocks per second, a gauge.</summary>
    public const string DeadlockRate = "sqlserver.deadlock.rate";

    /// <summary>Page life expectancy in seconds, a gauge.</summary>
    public const string PageLife = "sqlserver.page.life_expectancy";

    /// <summary>Logical CPUs, a gauge.</summary>
    public const string Cpus = "sqlserver.cpu.count";

    /// <summary>Memory SQL Server uses, in kilobytes.</summary>
    public const string Memory = "sqlserver.memory.usage";

    /// <summary>Seconds since the machine started, a gauge.</summary>
    public const string Uptime = "sqlserver.computer.uptime";

    /// <summary>Seconds waited per wait category and type, a cumulative sum.</summary>
    public const string WaitTime = "sqlserver.os.wait.duration";

    /// <summary>Seconds of file I/O per database file and direction, a cumulative sum.</summary>
    public const string FileLatency = "sqlserver.database.latency";

    /// <summary>File operations per database file and direction, a cumulative sum.</summary>
    public const string FileOperations = "sqlserver.database.operations";

    /// <summary>Data and log file bytes per database and file type, from <c>sqlquery</c>.</summary>
    public const string DatabaseSize = "sqlserver.database.size";

    /// <summary>Seconds since the last full backup per database, from <c>sqlquery</c>.</summary>
    public const string BackupAge = "sqlserver.database.backup.age";

    /// <summary>1 when a SQL Agent job's last run failed, from <c>sqlquery</c>.</summary>
    public const string JobFailed = "sqlserver.agent.job.failed";

    /// <summary>When a SQL Agent job's last run started, in Unix seconds, from <c>sqlquery</c>.</summary>
    public const string JobLastRun = "sqlserver.agent.job.last_run";

    /// <summary>How long a SQL Agent job's last run took, in seconds, from <c>sqlquery</c>.</summary>
    public const string JobDuration = "sqlserver.agent.job.duration";

    /// <summary>The longest running request per database now, in seconds, from <c>sqlquery</c>.</summary>
    public const string LongestQuery = "sqlserver.query.duration.max";

    /// <summary>The point attribute that names a database in <c>sqlquery</c>'s metrics.</summary>
    public const string DatabaseAttribute = "database_name";

    /// <summary>The resource attribute that names a database in the receiver's per-database metrics.</summary>
    public const string DatabaseResource = "sqlserver.database.name";

    /// <summary>The point attribute that names a SQL Agent job.</summary>
    public const string JobAttribute = "job_name";

    /// <summary>The event of a request running now.</summary>
    public const string QuerySample = "db.server.query_sample";

    /// <summary>Each statement's elapsed time, as what each minute added (VEY-43).</summary>
    public const string StatementElapsed = "sqlserver.statement.elapsed";

    /// <summary>Each statement's CPU time, as what each minute added.</summary>
    public const string StatementCpu = "sqlserver.statement.cpu";

    /// <summary>How many times each statement ran, as what each minute added.</summary>
    public const string StatementExecutions = "sqlserver.statement.executions";

    /// <summary>Each statement's pages read from memory, as what each minute added.</summary>
    public const string StatementReads = "sqlserver.statement.logical_reads";

    /// <summary>The statement metrics, which the database screen reads apart from the rest.</summary>
    public static readonly string[] Statements = [StatementElapsed, StatementCpu, StatementExecutions, StatementReads];

    /// <summary>
    /// What the database screen reads besides the statements. Naming them keeps a server with many statements from
    /// crowding these out of the series the screen gets.
    /// </summary>
    public static readonly string[] Screen =
    [
        BatchRequests, Connections, Blocked, DeadlockRate, PageLife, Cpus, Memory, Uptime, WaitTime, FileLatency,
        FileOperations, DatabaseSize, BackupAge, JobFailed, JobLastRun, JobDuration, LongestQuery,
    ];

    /// <summary>What the database screen's card reads, for the overview.</summary>
    public static readonly string[] Card =
    [
        BatchRequests, Connections, Blocked, DeadlockRate, PageLife, Cpus, Memory, Uptime, LongestQuery, BackupAge,
        JobFailed, DatabaseSize,
    ];
}
