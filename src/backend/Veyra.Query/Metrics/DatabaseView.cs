using System.Globalization;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics.Api;
using Veyra.Query.Systems;
using Veyra.Query.Systems.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Reads a SQL Server's metrics and query events from the tested Collector configuration (ADR 0018): requests,
/// blocking, deadlocks, waits, its databases, its SQL Agent jobs, and the queries running now.
/// </summary>
internal static class DatabaseView
{
    /// <summary>A request shorter than this is not listed as running: most finish before anyone could look.</summary>
    public static readonly TimeSpan LongRunning = TimeSpan.FromSeconds(10);

    // The receiver reports every request of one scrape at nearly the same time; this is how near.
    private static readonly TimeSpan _oneScrape = TimeSpan.FromSeconds(5);

    // The most lines of the waits chart; the rest of the categories are left out.
    private const int _maxWaitLines = 6;

    // The most queries of each list.
    private const int _maxQueries = 20;

    // The most top queries listed.
    private const int _maxTopQueries = 10;

    /// <summary>The numbers of a database server's card.</summary>
    /// <param name="block">The server.</param>
    /// <param name="window">The server's series over the chosen window.</param>
    /// <param name="recent">The same series over the last five minutes.</param>
    /// <param name="quarter">The deadlock series over the last 15 minutes in one-minute buckets.</param>
    /// <returns>The card.</returns>
    public static DatabaseCardResponse Card(
        TelemetryBlock block,
        SeriesData window,
        SeriesData recent,
        SeriesData quarter)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(quarter);

        var any = window.Series.Concat(recent.Series).ToList();
        var backups = LatestBy(recent, SqlServerNames.BackupAge, SqlServerNames.DatabaseAttribute);
        var longest = LatestBy(recent, SqlServerNames.LongestQuery, SqlServerNames.DatabaseAttribute);

        return new DatabaseCardResponse(
            Engine: block.Engine ?? block.Name,
            Server: block.Server ?? block.Name,
            Environment: any.Select(series => series.Environment).FirstOrDefault(value => value is not null),
            LastSeen: recent.LastSent(any, window.Window.End),
            Cpus: Latest(recent, SqlServerNames.Cpus) is { } cpus ? (int)cpus : null,
            MemoryBytes: Latest(recent, SqlServerNames.Memory) * 1024,
            UptimeSeconds: Latest(recent, SqlServerNames.Uptime),
            BatchRequests: recent.Latest(Counted(recent, SqlServerNames.BatchRequests)),
            Connections: Latest(recent, SqlServerNames.Connections),
            Blocked: Latest(recent, SqlServerNames.Blocked),
            Deadlocks: quarter.Named(SqlServerNames.DeadlockRate).Any()
                ? Math.Round(Deadlocks(quarter).Values.Sum())
                : null,
            PageLifeSeconds: Latest(recent, SqlServerNames.PageLife),
            LongestQuerySeconds: longest.Count > 0 ? longest.Values.Max() : null,
            Databases: DatabaseNames(recent).Count,
            OldestBackupSeconds: backups.Count > 0 ? backups.Values.Max() : null,
            FailedJobs: LatestBy(recent, SqlServerNames.JobFailed, SqlServerNames.JobAttribute)
                .Count(job => job.Value >= 1),
            Requests: window.Window.Line("batch requests", Counted(window, SqlServerNames.BatchRequests)));
    }

    /// <summary>A database server's screen.</summary>
    /// <param name="block">The server.</param>
    /// <param name="window">The server's series over the chosen window.</param>
    /// <param name="recent">The same series over the last five minutes.</param>
    /// <param name="quarter">The deadlock series over the last 15 minutes in one-minute buckets.</param>
    /// <param name="day">The database size series over the last day, for growth.</param>
    /// <param name="samples">The server's query samples of the last minute, newest first.</param>
    /// <param name="statements">The server's statements and what each added over the window.</param>
    /// <param name="system">
    /// Whether the server's own databases, jobs, and statements are listed (VEY-66); left out before any cut when not.
    /// </param>
    /// <returns>The screen.</returns>
    public static DatabaseMetricsResponse Detail(
        TelemetryBlock block,
        SeriesData window,
        SeriesData recent,
        SeriesData quarter,
        SeriesData day,
        IReadOnlyList<LogRecord> samples,
        StatementTotals statements,
        bool system)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(statements);

        var lines = window.Window;
        var (top, hiddenTop, agent) = TopQueries(statements, system);
        var (databases, hiddenDatabases) = SystemRows.Keep(
            Databases(window, recent, day),
            system,
            database => database.System,
            database => database.Name);
        var (jobs, hiddenJobs) = SystemRows.Keep(Jobs(recent), system, job => job.System, job => job.Name);

        return new DatabaseMetricsResponse(
            Card(block, window, recent, quarter),
            lines.Range,
            (int)lines.Bucket.TotalSeconds,
            lines.Line("batch requests", Counted(window, SqlServerNames.BatchRequests)),
            lines.Line("connections", Levels(window, SqlServerNames.Connections)),
            lines.Line("blocked", Levels(window, SqlServerNames.Blocked)),
            lines.Line("deadlocks", Deadlocks(window)),
            lines.Line("page life", Levels(window, SqlServerNames.PageLife)),
            lines.Line("longest query", Longest(window, SqlServerNames.LongestQuery)),
            Waits(window),
            databases,
            jobs,
            Running(samples),
            top)
        {
            From = lines.From,
            To = lines.End,
            AgentWork = agent,
            HiddenDatabases = hiddenDatabases,
            HiddenJobs = hiddenJobs,
            HiddenTopQueries = hiddenTop,
        };
    }

    /// <summary>
    /// The deadlocks of each bucket, from the server's running totals of them: SQL Server's for the server, or
    /// PostgreSQL's per database, added up. The first bucket of the window is the baseline, so deadlocks from before
    /// the window are never counted again.
    /// </summary>
    /// <param name="data">The server's series.</param>
    /// <returns>The deadlocks per bucket start.</returns>
    public static Dictionary<DateTimeOffset, double> Deadlocks(SeriesData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var seconds = data.Window.Bucket.TotalSeconds;

        return data.Rates(data.Named(SqlServerNames.DeadlockRate, MetricNames.PostgresDeadlocks))
            .ToDictionary(pair => pair.Key, pair => pair.Value * seconds);
    }

    /// <summary>The newest value of a metric's series, per value of an attribute, such as per database.</summary>
    /// <param name="data">The server's series.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="attribute">The attribute that tells the series apart.</param>
    /// <returns>The newest value per attribute value; series without a point in the window are left out.</returns>
    public static Dictionary<string, double> LatestBy(SeriesData data, string name, string attribute) =>
        LatestBy(data, name, series => MetricNames.Attribute(series, attribute));

    /// <summary>The newest value of a metric's series, per what tells them apart, such as their database.</summary>
    /// <param name="data">The server's series.</param>
    /// <param name="name">The metric name.</param>
    /// <param name="keyOf">What tells a series apart from the others, or null for a series to leave out.</param>
    /// <returns>The newest value per key; series without a point in the window are left out.</returns>
    public static Dictionary<string, double> LatestBy(SeriesData data, string name, Func<MetricSeries, string?> keyOf)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keyOf);

        return data.Named(name)
            .Select(series => (
                Key: keyOf(series),
                Value: data.Latest(data.Levels([series]))))
            .Where(pair => pair.Key is not null && pair.Value is not null)
            .GroupBy(pair => pair.Key!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value!.Value), StringComparer.Ordinal);
    }

    private static double? Latest(SeriesData data, string name) => data.Latest(Levels(data, name));

    // Per second, from a counter SQL Server keeps as a running total since it started. The receiver sends counters such
    // as "Batch Requests/sec" and "Number of Deadlocks/sec" as that total, under a name ending in ".rate", when it
    // reads over a connection.
    private static Dictionary<DateTimeOffset, double> Counted(SeriesData data, string name) =>
        data.Rates(data.Named(name));

    private static Dictionary<DateTimeOffset, double> Levels(SeriesData data, string name) =>
        data.Levels(data.Named(name));

    /// <summary>
    /// The highest value of any series of a metric per bucket, where adding them up would mean nothing, such as the
    /// longest query of each database.
    /// </summary>
    /// <param name="data">The server's series.</param>
    /// <param name="name">The metric name.</param>
    /// <returns>The highest value per bucket start.</returns>
    public static Dictionary<DateTimeOffset, double> Longest(SeriesData data, string name) =>
        data.Named(name)
            .SelectMany(series => data.Levels([series]))
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value));

    // Seconds waited per second for each category of wait, the largest categories first.
    internal static List<ChartLineResponse> Waits(SeriesData data) =>
        data.Named(SqlServerNames.WaitTime)
            .Where(series => !SystemObjects.IsIdleWait(MetricNames.Attribute(series, "wait.type")))
            .GroupBy(series => MetricNames.Attribute(series, "wait.category") ?? "Other", StringComparer.Ordinal)
            .Select(group => (Category: group.Key, Rates: data.Rates(group)))
            .Where(category => category.Rates.Values.Sum() > 0)
            .OrderByDescending(category => category.Rates.Values.Sum())
            .ThenBy(category => category.Category, StringComparer.Ordinal)
            .Take(_maxWaitLines)
            .Select(category => data.Window.Line(category.Category, category.Rates))
            .ToList();

    // The names of the server's databases, from whichever metric names them.
    private static HashSet<string> DatabaseNames(SeriesData data) =>
        data.Named(SqlServerNames.DatabaseSize, SqlServerNames.BackupAge, SqlServerNames.LongestQuery)
            .Select(series => MetricNames.Attribute(series, SqlServerNames.DatabaseAttribute))
            .Concat(data.Named(SqlServerNames.FileLatency)
                .Select(series => series.ResourceAttributes.GetValueOrDefault(SqlServerNames.DatabaseResource)))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>A SQL Server's databases, with their sizes, growth, backups, I/O, and longest queries.</summary>
    /// <param name="window">The server's series over the window, for the I/O latency.</param>
    /// <param name="recent">The same series over the last five minutes, for the figures that say now.</param>
    /// <param name="day">The database size series over the last day, for growth.</param>
    /// <returns>The databases, the largest first, the server's own marked <c>System</c>.</returns>
    public static List<DatabasePartResponse> Databases(SeriesData window, SeriesData recent, SeriesData day)
    {
        var sizes = recent.Named(SqlServerNames.DatabaseSize).ToList();
        var backups = LatestBy(recent, SqlServerNames.BackupAge, SqlServerNames.DatabaseAttribute);
        var longest = LatestBy(recent, SqlServerNames.LongestQuery, SqlServerNames.DatabaseAttribute);

        return DatabaseNames(recent)
            .Select(name =>
            {
                var own = sizes
                    .Where(series => MetricNames.Attribute(series, SqlServerNames.DatabaseAttribute) == name)
                    .ToList();
                var size = recent.Latest(recent.Levels(own));
                var log = recent.Latest(recent.Levels(own.Where(series =>
                    MetricNames.Attribute(series, "file_type") == "log")));

                return new DatabasePartResponse(
                    name,
                    size,
                    log,
                    Growth(day, name),
                    backups.TryGetValue(name, out var backup) ? backup : null,
                    IoLatency(window, name),
                    longest.TryGetValue(name, out var query) ? query : null)
                {
                    System = SystemObjects.IsSystemDatabase(SqlServerNames.Engine, name),
                };
            })
            .OrderByDescending(database => database.SizeBytes ?? 0)
            .ThenBy(database => database.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// How much a database grew per day, from its first and last size within the day read. The server's Overview uses
    /// it too (ADR 0025), so its inventory agrees with the database screen.
    /// </summary>
    /// <param name="day">The database size series over the last day, in hourly buckets.</param>
    /// <param name="name">The database.</param>
    /// <returns>Bytes per day, or null with fewer than two sizes.</returns>
    public static double? Growth(SeriesData day, string name)
    {
        ArgumentNullException.ThrowIfNull(day);

        var sizes = day.Levels(day.Named(SqlServerNames.DatabaseSize)
            .Where(series => MetricNames.Attribute(series, SqlServerNames.DatabaseAttribute) == name));

        if (sizes.Count < 2)
        {
            return null;
        }

        var first = sizes.MinBy(pair => pair.Key);
        var last = sizes.MaxBy(pair => pair.Key);
        var days = (last.Key - first.Key).TotalDays;

        return days > 0 ? (last.Value - first.Value) / days : null;
    }

    // The average seconds of one file read or write over the window: the time spent over the operations done.
    private static double? IoLatency(SeriesData window, string name)
    {
        bool Own(MetricSeries series) =>
            series.ResourceAttributes.GetValueOrDefault(SqlServerNames.DatabaseResource) == name;

        var seconds = window.Rates(window.Named(SqlServerNames.FileLatency).Where(Own)).Values.Sum();
        var operations = window.Rates(window.Named(SqlServerNames.FileOperations).Where(Own)).Values.Sum();

        return operations > 0 ? seconds / operations : null;
    }

    private static List<AgentJobResponse> Jobs(SeriesData recent)
    {
        var failed = LatestBy(recent, SqlServerNames.JobFailed, SqlServerNames.JobAttribute);
        var started = LatestBy(recent, SqlServerNames.JobLastRun, SqlServerNames.JobAttribute);
        var took = LatestBy(recent, SqlServerNames.JobDuration, SqlServerNames.JobAttribute);

        return failed.Keys
            .Concat(started.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(job => new AgentJobResponse(
                job,
                failed.GetValueOrDefault(job) >= 1,
                started.TryGetValue(job, out var at) ? DateTimeOffset.FromUnixTimeSeconds((long)at) : null,
                took.TryGetValue(job, out var seconds) ? seconds : null)
            {
                System = SystemObjects.IsSystemJob(job),
            })
            .OrderByDescending(job => job.Failed)
            .ThenBy(job => job.Name, StringComparer.Ordinal)
            .ToList();
    }

    // The requests of the newest scrape that have run for long: older samples are of requests that may have ended.
    private static List<RunningQueryResponse> Running(IReadOnlyList<LogRecord> samples)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        var newest = samples.Max(sample => sample.Timestamp);

        return samples
            .Where(sample => sample.Timestamp >= newest - _oneScrape)
            .GroupBy(sample => Text(sample, "sqlserver.session_id") ?? "", StringComparer.Ordinal)
            .Select(group => group.MaxBy(sample => sample.Timestamp)!)
            .Select(sample => new RunningQueryResponse(
                Text(sample, "sqlserver.session_id"),
                Text(sample, "db.namespace"),
                Text(sample, "user.name"),
                Number(sample, "sqlserver.total_elapsed_time") ?? 0,
                Text(sample, "sqlserver.request_status"),
                Text(sample, "sqlserver.wait_type"),
                Text(sample, "sqlserver.blocking_session_id") is { } blocker && blocker != "0" ? blocker : null,
                Text(sample, "db.query.text") ?? ""))
            .Where(query => query.ElapsedSeconds >= LongRunning.TotalSeconds)
            .OrderByDescending(query => query.ElapsedSeconds)
            .Take(_maxQueries)
            .ToList();
    }

    // The statements with the most time over the window, from what each minute added (VEY-43). The agent's own
    // statements are left out of the ranking, so the server's own work is what the list shows, and summed apart. The
    // server's own statements are left out unless asked for, and ranked apart when they are (VEY-64, VEY-66).
    private static (List<TopQueryResponse> Top, HiddenRowsResponse Hidden, AgentWorkResponse? Agent) TopQueries(
        StatementTotals statements,
        bool system)
    {
        var added = statements.Buckets
            .GroupBy(bucket => bucket.SeriesId)
            .ToDictionary(group => group.Key, group => group.Sum(bucket => bucket.Total ?? 0));

        var all = statements.Series
            .GroupBy(series => (
                Hash: MetricNames.Attribute(series, "query_hash") ?? "",
                Database: MetricNames.Attribute(series, "database_name") ?? ""))
            .Select(statement =>
            {
                double Sum(string name) =>
                    statement.Where(series => series.Name == name).Sum(series => added.GetValueOrDefault(series.Id));

                var first = statement.First();

                return (
                    Text: MetricNames.Attribute(first, "query_text") ?? "",
                    statement.Key.Database,
                    Agent: MetricNames.Attribute(first, "agent") == "true",
                    Seconds: Sum(SqlServerNames.StatementElapsed),
                    Cpu: Sum(SqlServerNames.StatementCpu),
                    Runs: (long)Sum(SqlServerNames.StatementExecutions),
                    Reads: (long)Sum(SqlServerNames.StatementReads));
            })
            .Where(statement => statement.Seconds > 0 || statement.Runs > 0)
            .ToList();

        var (listed, hidden) = SystemRows.Keep(
            all.Where(statement => !statement.Agent),
            system,
            statement => SystemObjects.IsSystemStatement(statement.Database, statement.Text),
            statement => SystemRows.SystemDatabase(SqlServerNames.Engine, statement.Database));
        var top = listed
            .GroupBy(statement => SystemObjects.IsSystemStatement(statement.Database, statement.Text))
            .SelectMany(group => group.OrderByDescending(statement => statement.Seconds).Take(_maxTopQueries))
            .OrderByDescending(statement => statement.Seconds)
            .Select(statement => new TopQueryResponse(
                statement.Text,
                statement.Seconds,
                statement.Runs,
                statement.Cpu,
                statement.Reads)
            {
                Database = statement.Database.Length > 0 ? statement.Database : null,
                System = SystemObjects.IsSystemStatement(statement.Database, statement.Text),
            })
            .ToList();

        var agent = all.Where(statement => statement.Agent).ToList();

        return (
            top,
            hidden,
            agent.Count == 0 ? null : new AgentWorkResponse(agent.Count, agent.Sum(one => one.Seconds)));
    }

    private static string? Text(LogRecord record, string key) =>
        record.Attributes.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static double? Number(LogRecord record, string key) =>
        Text(record, key) is { } text
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
