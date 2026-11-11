using System.Globalization;
using Npgsql;

namespace Veyra.Storage.Schema;

/// <summary>
/// Creates and lists the daily partitions of a table partitioned by a <c>timestamp</c> range. Logs and metric points
/// both use them, so retention can drop whole days.
/// </summary>
internal static class DailyPartitions
{
    // PostgreSQL's error code for a violated check, raised when the default partition already holds rows for the day.
    private const string _checkViolation = "23514";

    /// <summary>The partition's name for a day, for example <c>metric_point_20261110</c>.</summary>
    /// <param name="table">The partitioned table in the <c>storage</c> schema.</param>
    /// <param name="day">The day.</param>
    /// <returns>The name.</returns>
    public static string Name(string table, DateOnly day) =>
        $"{table}_{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";

    /// <summary>Creates the partition of one UTC day if it does not exist.</summary>
    /// <param name="dataSource">The PostgreSQL connection pool.</param>
    /// <param name="table">The partitioned table in the <c>storage</c> schema; a constant, never user input.</param>
    /// <param name="day">The day.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// <see langword="false"/> when the default partition already holds rows for the day, so the day cannot get its
    /// own.
    /// </returns>
    public static async Task<bool> EnsureDayAsync(
        NpgsqlDataSource dataSource,
        string table,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var from = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Partition bounds cannot be parameters, so the SQL is built from a date this method formatted itself.
        await using var command = dataSource.CreateCommand(
            $"""
            CREATE TABLE IF NOT EXISTS storage.{Name(table, day)}
                PARTITION OF storage.{table}
                FOR VALUES FROM ('{from} 00:00:00+00') TO ('{to} 00:00:00+00')
            """);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);

            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == _checkViolation)
        {
            return false;
        }
    }

    /// <summary>Lists the daily partitions of a table with their day and size, oldest first.</summary>
    /// <param name="dataSource">The PostgreSQL connection pool.</param>
    /// <param name="table">The partitioned table in the <c>storage</c> schema.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The partitions; the default partition is not among them.</returns>
    public static async Task<IReadOnlyList<(string Name, DateOnly Day, long Bytes)>> ListAsync(
        NpgsqlDataSource dataSource,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT child.relname, pg_total_relation_size(child.oid)
            FROM pg_inherits
            JOIN pg_class AS child ON child.oid = pg_inherits.inhrelid
            JOIN pg_class AS parent ON parent.oid = pg_inherits.inhparent
            JOIN pg_namespace ON pg_namespace.oid = parent.relnamespace
            WHERE pg_namespace.nspname = 'storage' AND parent.relname = $1
            """);
        command.Parameters.AddWithValue(table);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var prefix = table + "_";
        var partitions = new List<(string Name, DateOnly Day, long Bytes)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);

            // Only names this class created: the table, an underscore, and a date. The default partition is skipped.
            if (name.StartsWith(prefix, StringComparison.Ordinal)
                && DateOnly.TryParseExact(
                    name[prefix.Length..],
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var day))
            {
                partitions.Add((name, day, reader.GetInt64(1)));
            }
        }

        return partitions.OrderBy(p => p.Day).ToList();
    }
}
