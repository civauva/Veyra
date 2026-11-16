using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Telemetry;
using Veyra.Query.Live;
using Veyra.Query.Queries.Api;
using Veyra.Query.Storage.Api;

namespace Veyra.Query.Storage;

/// <summary>
/// Builds a SQL Server's index and statistics advice (ADR 0020, area 9) from the agent's hourly read: missing,
/// unused, and duplicate indexes, heaps, stale statistics, and lock escalations, each with the statement to run.
/// </summary>
/// <param name="activity">The index records.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class IndexViewBuilder(IDatabaseActivitySearch activity, TimeProvider clock)
{
    /// <summary>The kind of record an index row is.</summary>
    public const string IndexKind = "sqlserver.index";

    private const int _maxRows = 20_000;

    /// <summary>Builds the advice.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The advice.</returns>
    public async Task<IndexesResponse> IndexesAsync(string engine, string server, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var records = await activity.ListRecordsAsync(
            TelemetryBlock.Database(engine, server).Name,
            IndexKind,
            now.AddDays(-2),
            now,
            null,
            _maxRows,
            cancellationToken);

        return Build(records, now);
    }

    /// <summary>Turns the rows into advice. Pure; tested without a database.</summary>
    /// <param name="records">The index rows, newest first.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The advice.</returns>
    public static IndexesResponse Build(IReadOnlyList<ActivityRecord> records, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);

        var rows = records.GroupBy(record => record.Key).Select(group => group.First()).ToList();
        var findings = new List<PlanWarningResponse>();

        foreach (var missing in rows.Where(row => Text(row, "row_type") == "missing")
            .Where(row => Number(row, "impact") >= 50 && Number(row, "reads") >= 100)
            .OrderByDescending(row => Number(row, "impact") * Number(row, "reads"))
            .Take(20))
        {
            var table = Text(missing, "table_name");
            var keys = string.Join(
                ", ",
                new[] { missing.Body, Text(missing, "more_columns") }.Where(part => part.Length > 0));
            var include = Text(missing, "include_columns");
            var impact = Number(missing, "impact");
            var reads = Number(missing, "reads");
            var name = "IX_"
                + Plain(table.Split('.')[^1])
                + "_"
                + Plain(keys).Replace(", ", "_", StringComparison.Ordinal);

            findings.Add(new PlanWarningResponse(
                "missing-index",
                impact >= 80 ? "warning" : "caution",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"An index on {Plain(table)} would save {impact:0}% for {reads:N0} reads"),
                "SQL Server noted this index as missing while it ran queries since it last started.",
                "A DBA can add it; it makes reads faster and every write to the table a little slower.",
                $"CREATE INDEX [{name}] ON {table} ({keys})"
                + (include.Length > 0 ? $" INCLUDE ({include})" : "")
                + ";"));
        }

        var usage = rows.Where(row => Text(row, "row_type") == "usage").ToList();

        foreach (var unused in usage
            .Where(row => Text(row, "index_type") == "NONCLUSTERED" && Number(row, "is_unique") == 0
                && Number(row, "reads") == 0 && Number(row, "writes") > 0)
            .OrderByDescending(row => Number(row, "writes"))
            .Take(20))
        {
            var index = Text(unused, "index_name");
            var table = Text(unused, "table_name");
            var database = Text(unused, "database_name");
            var writes = Number(unused, "writes");

            findings.Add(new PlanWarningResponse(
                "unused-index",
                "caution",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Index {index} on {table} in {database} was never read, but written {writes:N0} times"),
                $"It takes {Size(Number(unused, "size_bytes"))} and slows every write. Counted since SQL Server last "
                + "started, so check it has run through a whole month first.",
                "Drop it if nothing needs it, such as a report run once a month.",
                $"USE [{Text(unused, "database_name")}]; DROP INDEX [{Text(unused, "index_name")}] ON "
                + $"{Text(unused, "table_name")};"));
        }

        foreach (var duplicate in usage
            .Where(row => Text(row, "index_type") == "NONCLUSTERED" && row.Body.Length > 0)
            .GroupBy(row => (
                Database: Text(row, "database_name"),
                Table: Text(row, "table_name"),
                Keys: row.Body,
                Include: Text(row, "include_columns")))
            .Where(group => group.Count() > 1))
        {
            var drop = duplicate.OrderBy(row => Number(row, "is_unique")).ThenBy(row => Number(row, "reads")).First();

            findings.Add(new PlanWarningResponse(
                "duplicate-index",
                "caution",
                $"{Words.Count(duplicate.Count(), "index")} on {duplicate.Key.Table} in {duplicate.Key.Database} have "
                + $"the same keys ({duplicate.Key.Keys})",
                "Each one costs space and every write, and only one is needed.",
                $"Drop {Text(drop, "index_name")}, the one read least, unless it enforces uniqueness.",
                $"USE [{duplicate.Key.Database}]; DROP INDEX [{Text(drop, "index_name")}] ON {duplicate.Key.Table};"));
        }

        foreach (var heap in usage
            .Where(row => Text(row, "index_type") == "HEAP" && Number(row, "size_bytes") >= 100 * 1024 * 1024))
        {
            findings.Add(new PlanWarningResponse(
                "heap",
                "caution",
                $"{Text(heap, "table_name")} in {Text(heap, "database_name")} has no clustered index "
                + $"({Size(Number(heap, "size_bytes"))})",
                "A large table without one leaves forwarded rows and space behind as rows change, and reads more "
                + "than it needs.",
                "Give it a clustered index, usually on its primary key.",
                null));
        }

        foreach (var stats in rows.Where(row => Text(row, "row_type") == "stats")
            .Where(row => Number(row, "updated") > 0
                && now - DateTimeOffset.FromUnixTimeSeconds((long)Number(row, "updated")) > TimeSpan.FromDays(7))
            .OrderBy(row => Number(row, "updated"))
            .Take(20))
        {
            var age = now - DateTimeOffset.FromUnixTimeSeconds((long)Number(stats, "updated"));
            var tableRows = Number(stats, "table_rows");

            findings.Add(new PlanWarningResponse(
                "stale-statistics",
                age > TimeSpan.FromDays(30) ? "warning" : "caution",
                $"Statistics {Text(stats, "index_name")} on {Text(stats, "table_name")} in "
                + $"{Text(stats, "database_name")} were last updated {Words.Duration(age.TotalSeconds)} ago",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The table has {tableRows:N0} rows. Old statistics lead to bad row estimates and slow plans."),
                "Update them, and check the maintenance job does.",
                $"USE [{Text(stats, "database_name")}]; UPDATE STATISTICS {Text(stats, "table_name")} "
                + $"[{Text(stats, "index_name")}];"));
        }

        foreach (var escalation in usage
            .Where(row => Number(row, "escalations") >= 100)
            .OrderByDescending(row => Number(row, "escalations"))
            .Take(10))
        {
            var tableName = Text(escalation, "table_name");
            var databaseName = Text(escalation, "database_name");
            var escalations = Number(escalation, "escalations");

            findings.Add(new PlanWarningResponse(
                "lock-escalation",
                "caution",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Locks on {tableName} in {databaseName} escalated {escalations:N0} times"),
                "When a statement locks many rows, SQL Server locks the whole table instead, and everyone else waits.",
                "Change fewer rows per statement, in batches, or index the statement's filter so it touches fewer "
                + "rows.",
                null));
        }

        var indexes = usage
            .OrderByDescending(row => Number(row, "size_bytes"))
            .Take(100)
            .Select(row => new IndexResponse(
                Text(row, "database_name"),
                Text(row, "table_name"),
                Text(row, "index_name"),
                Text(row, "index_type"),
                row.Body,
                Text(row, "include_columns"),
                Number(row, "size_bytes"),
                Number(row, "reads"),
                Number(row, "writes"),
                Number(row, "escalations")))
            .ToList();

        return new IndexesResponse(
            findings.OrderBy(finding => finding.Severity == "warning" ? 0 : 1).ToList(),
            indexes);
    }

    private static string Text(ActivityRecord record, string key) => record.Attributes.GetValueOrDefault(key) ?? "";

    private static double Number(ActivityRecord record, string key) =>
        double.TryParse(Text(record, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string Plain(string name) =>
        name.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal);

    private static string Size(double bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1L << 30):0.#} GB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1L << 20):0} MB"),
    };
}
