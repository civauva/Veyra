using Veyra.Core.Metrics;

namespace Veyra.Query.Metrics;

/// <summary>
/// The metric and attribute names Veyra reads, from the Collector's <c>hostmetrics</c>, <c>docker_stats</c>, and
/// <c>postgresql</c> receivers, the <c>sqlquery</c> metrics of ADR 0018, and the OpenTelemetry semantic conventions.
/// Where the conventions renamed an attribute, both names are read.
/// </summary>
internal static class MetricNames
{
    /// <summary>CPU time per CPU and state, a cumulative sum in seconds.</summary>
    public const string CpuTime = "system.cpu.time";

    /// <summary>Memory per state, in bytes.</summary>
    public const string Memory = "system.memory.usage";

    /// <summary>Filesystem space per mount point and state, in bytes.</summary>
    public const string Filesystem = "system.filesystem.usage";

    /// <summary>The load averages.</summary>
    public const string Load1 = "system.cpu.load_average.1m";

    /// <summary>The five-minute load average.</summary>
    public const string Load5 = "system.cpu.load_average.5m";

    /// <summary>The fifteen-minute load average.</summary>
    public const string Load15 = "system.cpu.load_average.15m";

    /// <summary>Network bytes per device and direction, a cumulative sum.</summary>
    public const string NetworkIo = "system.network.io";

    /// <summary>Disk bytes per device and direction, a cumulative sum.</summary>
    public const string DiskIo = "system.disk.io";

    /// <summary>HTTP server request durations, a histogram in seconds.</summary>
    public const string RequestDuration = "http.server.request.duration";

    /// <summary>Process CPU time of .NET, a cumulative sum in seconds.</summary>
    public const string DotnetCpuTime = "dotnet.process.cpu.time";

    /// <summary>Process CPU time of other runtimes.</summary>
    public const string ProcessCpuTime = "process.cpu.time";

    /// <summary>How many CPUs a .NET process may use.</summary>
    public const string DotnetCpuCount = "dotnet.process.cpu.count";

    /// <summary>The working set of a .NET process, in bytes.</summary>
    public const string DotnetWorkingSet = "dotnet.process.memory.working_set";

    /// <summary>The memory of a process of other runtimes, in bytes.</summary>
    public const string ProcessMemory = "process.memory.usage";

    /// <summary>.NET garbage collections, a cumulative count.</summary>
    public const string DotnetGcCollections = "dotnet.gc.collections";

    /// <summary>Work items waiting in the .NET thread pool.</summary>
    public const string DotnetThreadPoolQueue = "dotnet.thread_pool.queue.length";

    /// <summary>Threads in the .NET thread pool.</summary>
    public const string DotnetThreadPoolThreads = "dotnet.thread_pool.thread.count";

    /// <summary>
    /// A container's CPU in use, from the Collector's <c>docker_stats</c> receiver: a gauge in percent of one CPU, so
    /// 12.5 is an eighth of a CPU and 150 is one and a half (ADR 0018).
    /// </summary>
    public const string ContainerCpu = "container.cpu.utilization";

    /// <summary>A container's memory in use without the page cache, in bytes: a sum that can fall.</summary>
    public const string ContainerMemoryUsage = "container.memory.usage.total";

    /// <summary>
    /// A container's memory limit in bytes, a sum that can fall. Docker reports the host's memory for a container
    /// without one, and 0 for usage and limit alike while it is between restarts.
    /// </summary>
    public const string ContainerMemoryLimit = "container.memory.usage.limit";

    /// <summary>A container's memory in use of its limit, a gauge from 0 to 100.</summary>
    public const string ContainerMemoryPercent = "container.memory.percent";

    /// <summary>Docker's count of a container's restarts, a cumulative sum.</summary>
    public const string ContainerRestarts = "container.restarts";

    /// <summary>How long a container has run since it last started, a gauge in seconds.</summary>
    public const string ContainerUptime = "container.uptime";

    /// <summary>The resource attribute that names a container's image.</summary>
    public const string ContainerImage = "container.image.name";

    /// <summary>The resource attribute that holds a container's ID.</summary>
    public const string ContainerId = "container.id";

    /// <summary>Every metric a container view and the container rules read.</summary>
    public static readonly string[] Container =
    [
        ContainerCpu,
        ContainerMemoryUsage,
        ContainerMemoryLimit,
        ContainerMemoryPercent,
        ContainerRestarts,
        ContainerUptime,
    ];

    /// <summary>The engine of a PostgreSQL server, as <c>db.system.name</c> spells it (ADR 0018).</summary>
    public const string PostgresEngine = "postgresql";

    /// <summary>
    /// Connections to one database now, from the <c>postgresql</c> receiver: a sum that can fall, with the database on
    /// the resource.
    /// </summary>
    public const string PostgresBackends = "postgresql.backends";

    /// <summary>The server's maximum connections, its <c>max_connections</c> setting.</summary>
    public const string PostgresMaxConnections = "postgresql.connection.max";

    /// <summary>A database's size in bytes, with the database on the resource.</summary>
    public const string PostgresDatabaseSize = "postgresql.db_size";

    /// <summary>A database's deadlocks, a cumulative sum, with the database on the resource.</summary>
    public const string PostgresDeadlocks = "postgresql.deadlocks";

    /// <summary>
    /// A table's rows, live and dead apart in <see cref="PostgresRowState"/>, with the database, schema, and table on
    /// the resource.
    /// </summary>
    public const string PostgresRows = "postgresql.rows";

    /// <summary>How many times a table was vacuumed, a cumulative sum per table.</summary>
    public const string PostgresVacuums = "postgresql.table.vacuum.count";

    /// <summary>A database's committed transactions, a cumulative sum.</summary>
    public const string PostgresCommits = "postgresql.commits";

    /// <summary>A database's rolled back transactions, a cumulative sum.</summary>
    public const string PostgresRollbacks = "postgresql.rollbacks";

    /// <summary>
    /// The age of a database's oldest unfrozen transaction ID, from <c>sqlquery</c>: a gauge, with the database on the
    /// point (ADR 0018).
    /// </summary>
    public const string PostgresTransactionIdAge = "postgresql.database.xid.age";

    /// <summary>Sessions waiting for a lock longer than 30 seconds, from <c>sqlquery</c>: a gauge per server.</summary>
    public const string PostgresBlocked = "postgresql.sessions.blocked";

    /// <summary>The longest running query now in seconds, from <c>sqlquery</c>, the database on the point.</summary>
    public const string PostgresLongestQuery = "postgresql.query.duration.max";

    /// <summary>
    /// The attribute that names a PostgreSQL database: on the resource for the receiver's metrics, on the point for
    /// <c>sqlquery</c>'s.
    /// </summary>
    public const string PostgresDatabase = "postgresql.database.name";

    /// <summary>The resource attribute that names a table's schema.</summary>
    public const string PostgresSchema = "postgresql.schema.name";

    /// <summary>The resource attribute that names a table.</summary>
    public const string PostgresTable = "postgresql.table.name";

    /// <summary>The point attribute of <see cref="PostgresRows"/>: <c>live</c> or <c>dead</c>.</summary>
    public const string PostgresRowState = "state";

    /// <summary>
    /// How old a transaction ID may grow before PostgreSQL would wrap around: 2^31. The server stops accepting writes
    /// shortly before it.
    /// </summary>
    public const double PostgresWraparoundLimit = 2_147_483_648;

    /// <summary>What a PostgreSQL server's screen and its card on the overview read besides its tables.</summary>
    public static readonly string[] PostgresScreen =
    [
        PostgresBackends, PostgresMaxConnections, PostgresDatabaseSize, PostgresDeadlocks, PostgresCommits,
        PostgresRollbacks, PostgresTransactionIdAge, PostgresBlocked, PostgresLongestQuery,
    ];

    /// <summary>
    /// What a PostgreSQL server's screen reads of its tables: two row series and one vacuum series per table, read
    /// apart so a server with many tables does not crowd out the rest.
    /// </summary>
    public static readonly string[] PostgresTables = [PostgresRows, PostgresVacuums];

    /// <summary>What the database rules judge of a PostgreSQL server.</summary>
    public static readonly string[] PostgresRules =
    [
        PostgresBackends, PostgresMaxConnections, PostgresDeadlocks, PostgresTransactionIdAge, PostgresBlocked,
        PostgresLongestQuery,
    ];

    /// <summary>The PostgreSQL database a series is about, from its point or else its resource.</summary>
    /// <param name="series">The series.</param>
    /// <returns>The database name, or null for a server-wide series.</returns>
    public static string? PostgresDatabaseOf(MetricSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return Attribute(series, PostgresDatabase) ?? series.ResourceAttributes.GetValueOrDefault(PostgresDatabase);
    }

    /// <summary>Every metric a host view reads.</summary>
    public static readonly string[] Host = [CpuTime, Memory, Filesystem, Load1, Load5, Load15, NetworkIo, DiskIo];

    /// <summary>Every metric a service view reads.</summary>
    public static readonly string[] Service =
    [
        RequestDuration,
        DotnetCpuTime,
        ProcessCpuTime,
        DotnetCpuCount,
        DotnetWorkingSet,
        ProcessMemory,
        DotnetGcCollections,
        DotnetThreadPoolQueue,
        DotnetThreadPoolThreads,
    ];

    /// <summary>The first of several attribute names a series has.</summary>
    /// <param name="series">The series.</param>
    /// <param name="keys">The names, newest convention last is fine; the first present wins.</param>
    /// <returns>The value, or null when none is present.</returns>
    public static string? Attribute(MetricSeries series, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(series);

        foreach (var key in keys)
        {
            if (series.Attributes.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
