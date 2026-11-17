using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Reads a PostgreSQL server's metrics from the tested Collector configuration (ADR 0018): connections of the
/// maximum, blocked sessions, deadlocks, transactions, the longest running query, and per database its size, its
/// transaction ID age of the wraparound limit, and the dead rows its vacuums have yet to clear.
/// </summary>
/// <remarks>
/// The <c>postgresql</c> receiver names a database on the resource, and <c>sqlquery</c> on the point; every
/// per-database figure reads both (<see cref="MetricNames.PostgresDatabaseOf"/>).
/// </remarks>
internal static class PostgresView
{
    // The most tables listed: the ones with the most dead rows.
    private const int _maxTables = 10;

    /// <summary>The numbers of a PostgreSQL server's card, and the head of its screen.</summary>
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
        var longest = ByDatabase(recent, MetricNames.PostgresLongestQuery);
        var ages = ByDatabase(recent, MetricNames.PostgresTransactionIdAge);

        return new DatabaseCardResponse(
            Engine: block.Engine ?? block.Name,
            Server: block.Server ?? block.Name,
            Environment: any.Select(series => series.Environment).FirstOrDefault(value => value is not null),
            LastSeen: recent.LastSent(any, window.Window.End),
            Cpus: null,
            MemoryBytes: null,
            UptimeSeconds: null,
            BatchRequests: null,
            Connections: recent.Latest(Levels(recent, MetricNames.PostgresBackends)),
            Blocked: recent.Latest(Levels(recent, MetricNames.PostgresBlocked)),
            Deadlocks: quarter.Named(MetricNames.PostgresDeadlocks).Any()
                ? Math.Round(DatabaseView.Deadlocks(quarter).Values.Sum())
                : null,
            PageLifeSeconds: null,
            LongestQuerySeconds: longest.Count > 0 ? longest.Values.Max() : null,
            Databases: DatabaseNames(recent).Count,
            OldestBackupSeconds: null,
            FailedJobs: 0,
            Requests: window.Window.Line("transactions", Transactions(window)))
        {
            MaxConnections = recent.Latest(DatabaseView.Longest(recent, MetricNames.PostgresMaxConnections)),
            CommitsPerSecond = recent.Latest(Counted(recent, MetricNames.PostgresCommits)),
            RollbacksPerSecond = recent.Latest(Counted(recent, MetricNames.PostgresRollbacks)),
            WraparoundRatio = ages.Count > 0 ? ages.Values.Max() / MetricNames.PostgresWraparoundLimit : null,
        };
    }

    /// <summary>A PostgreSQL server's screen.</summary>
    /// <param name="block">The server.</param>
    /// <param name="window">The server's series over the chosen window, its tables left out.</param>
    /// <param name="recent">The server's series and its tables' rows over the last five minutes.</param>
    /// <param name="quarter">The deadlock series over the last 15 minutes in one-minute buckets.</param>
    /// <param name="day">The database sizes and the tables' vacuums over the last day.</param>
    /// <returns>The screen.</returns>
    public static DatabaseMetricsResponse Detail(
        TelemetryBlock block,
        SeriesData window,
        SeriesData recent,
        SeriesData quarter,
        SeriesData day)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(day);

        var lines = window.Window;
        var tables = Tables(recent, day);

        return new DatabaseMetricsResponse(
            Card(block, window, recent, quarter),
            lines.Range,
            (int)lines.Bucket.TotalSeconds,
            lines.Line("transactions", Transactions(window)),
            lines.Line("connections", Levels(window, MetricNames.PostgresBackends)),
            lines.Line("blocked", Levels(window, MetricNames.PostgresBlocked)),
            lines.Line("deadlocks", DatabaseView.Deadlocks(window)),
            lines.Line("page life", new Dictionary<DateTimeOffset, double>()),
            lines.Line("longest query", DatabaseView.Longest(window, MetricNames.PostgresLongestQuery)),
            [],
            Databases(recent, day, tables),
            [],
            [],
            [])
        {
            MaxConnections = lines.Line(
                "maximum",
                DatabaseView.Longest(window, MetricNames.PostgresMaxConnections)),
            Commits = lines.Line("commits", Counted(window, MetricNames.PostgresCommits)),
            Rollbacks = lines.Line("rollbacks", Counted(window, MetricNames.PostgresRollbacks)),
            Tables = tables
                .Where(table => table.DeadRows > 0)
                .OrderByDescending(table => table.DeadRows)
                .ThenBy(table => table.Database, StringComparer.Ordinal)
                .ThenBy(table => table.Name, StringComparer.Ordinal)
                .Take(_maxTables)
                .ToList(),
        };
    }

    /// <summary>The connections in use of the server's maximum now, as the connections rule judges it.</summary>
    /// <param name="recent">The server's series over the last five minutes.</param>
    /// <returns>The share from 0 to 1, or null when the server does not say its maximum or its connections.</returns>
    public static double? ConnectionsRatio(SeriesData recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        var connections = recent.Latest(Levels(recent, MetricNames.PostgresBackends));
        var maximum = recent.Latest(DatabaseView.Longest(recent, MetricNames.PostgresMaxConnections));

        return connections is { } used && maximum is > 0 ? used / maximum.Value : null;
    }

    /// <summary>The newest value of a metric per PostgreSQL database, wherever the series names it.</summary>
    /// <param name="data">The server's series.</param>
    /// <param name="name">The metric name.</param>
    /// <returns>The newest value per database.</returns>
    public static Dictionary<string, double> ByDatabase(SeriesData data, string name) =>
        DatabaseView.LatestBy(data, name, MetricNames.PostgresDatabaseOf);

    // Transactions committed or rolled back per second, of every database.
    private static Dictionary<DateTimeOffset, double> Transactions(SeriesData data) =>
        data.Rates(data.Named(MetricNames.PostgresCommits, MetricNames.PostgresRollbacks));

    private static Dictionary<DateTimeOffset, double> Counted(SeriesData data, string name) =>
        data.Rates(data.Named(name));

    private static Dictionary<DateTimeOffset, double> Levels(SeriesData data, string name) =>
        data.Levels(data.Named(name));

    // The databases any per-database metric names.
    private static HashSet<string> DatabaseNames(SeriesData data) =>
        data.Named(MetricNames.PostgresScreen)
            .Select(MetricNames.PostgresDatabaseOf)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static List<DatabasePartResponse> Databases(
        SeriesData recent,
        SeriesData day,
        IReadOnlyList<DatabaseTableResponse> tables)
    {
        var sizes = ByDatabase(recent, MetricNames.PostgresDatabaseSize);
        var ages = ByDatabase(recent, MetricNames.PostgresTransactionIdAge);
        var longest = ByDatabase(recent, MetricNames.PostgresLongestQuery);

        return DatabaseNames(recent)
            .Union(tables.Select(table => table.Database), StringComparer.Ordinal)
            .Select(name =>
            {
                var own = tables.Where(table => table.Database == name).ToList();
                var live = Total(own.Select(table => table.LiveRows));
                var dead = Total(own.Select(table => table.DeadRows));
                var age = ages.TryGetValue(name, out var transactions) ? transactions : (double?)null;

                return new DatabasePartResponse(
                    name,
                    sizes.TryGetValue(name, out var size) ? size : null,
                    null,
                    Growth(day, name),
                    null,
                    null,
                    longest.TryGetValue(name, out var query) ? query : null)
                {
                    Connections = recent.Latest(recent.Levels(recent.Named(MetricNames.PostgresBackends)
                        .Where(series => MetricNames.PostgresDatabaseOf(series) == name))),
                    TransactionIdAge = age,
                    WraparoundRatio = age / MetricNames.PostgresWraparoundLimit,
                    LiveRows = live,
                    DeadRows = dead,
                    DeadRowsRatio = Share(dead, live),
                    Vacuums = Total(own.Select(table => table.Vacuums)),
                };
            })
            .OrderByDescending(database => database.SizeBytes ?? 0)
            .ThenBy(database => database.Name, StringComparer.Ordinal)
            .ToList();
    }

    // Every table that sends its rows, with its live and dead rows now and its vacuums over the day.
    private static List<DatabaseTableResponse> Tables(SeriesData recent, SeriesData day)
    {
        var vacuums = day.Named(MetricNames.PostgresVacuums)
            .GroupBy(TableOf)
            .ToDictionary(group => group.Key, group => Increase(day, group));

        return recent.Named(MetricNames.PostgresRows)
            .GroupBy(TableOf)
            .Where(table => table.Key.Database is not null && table.Key.Name is not null)
            .Select(table =>
            {
                double? Rows(string state) => recent.Latest(recent.Levels(table
                    .Where(series => MetricNames.Attribute(series, MetricNames.PostgresRowState) == state)));

                var live = Rows("live");
                var dead = Rows("dead");

                return new DatabaseTableResponse(
                    table.Key.Database!,
                    table.Key.Schema,
                    table.Key.Name!,
                    live,
                    dead,
                    Share(dead, live),
                    vacuums.TryGetValue(table.Key, out var count) ? count : null);
            })
            .ToList();
    }

    // The receiver names a table with its schema, such as public.orders, unless its separate schema attribute is on.
    private static (string? Database, string? Schema, string? Name) TableOf(MetricSeries series)
    {
        var schema = Named(series, MetricNames.PostgresSchema);
        var table = Named(series, MetricNames.PostgresTable);

        if (schema is null && table?.Split('.', 2) is [var inName, var rest])
        {
            (schema, table) = (inName, rest);
        }

        return (MetricNames.PostgresDatabaseOf(series), schema, table);
    }

    private static string? Named(MetricSeries series, string key) =>
        series.ResourceAttributes.GetValueOrDefault(key) ?? MetricNames.Attribute(series, key);

    // What running totals added over the data's window: each bucket against the one before, a fall being a restart.
    private static double? Increase(SeriesData data, IEnumerable<MetricSeries> series)
    {
        double? total = null;

        foreach (var one in series)
        {
            var values = data.Buckets.TryGetValue(one.Id, out var buckets)
                ? buckets.OrderBy(bucket => bucket.Start).Select(bucket => bucket.LastValue).OfType<double>().ToList()
                : [];

            for (var index = 1; index < values.Count; index++)
            {
                var (before, after) = (values[index - 1], values[index]);
                total = (total ?? 0) + (after >= before ? after - before : after);
            }

            if (values.Count == 1)
            {
                total ??= 0;
            }
        }

        return total;
    }

    // How much a database grew per day, from its first and last size within the day read.
    private static double? Growth(SeriesData day, string name)
    {
        var sizes = day.Levels(day.Named(MetricNames.PostgresDatabaseSize)
            .Where(series => MetricNames.PostgresDatabaseOf(series) == name));

        if (sizes.Count < 2)
        {
            return null;
        }

        var first = sizes.MinBy(pair => pair.Key);
        var last = sizes.MaxBy(pair => pair.Key);
        var days = (last.Key - first.Key).TotalDays;

        return days > 0 ? (last.Value - first.Value) / days : null;
    }

    private static double? Total(IEnumerable<double?> values)
    {
        var known = values.OfType<double>().ToList();

        return known.Count > 0 ? known.Sum() : null;
    }

    private static double? Share(double? dead, double? live) =>
        dead is { } part && live is { } rest && part + rest > 0 ? part / (part + rest) : null;
}
