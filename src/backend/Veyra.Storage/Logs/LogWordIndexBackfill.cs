using System.Globalization;
using Npgsql;

namespace Veyra.Storage.Logs;

/// <summary>
/// After an upgrade, builds the index of words by hour (ADR 0016) on each day written before it existed, newest day
/// first, and drops the old word index once every day has the new one.
/// </summary>
/// <remarks>
/// <c>CREATE INDEX CONCURRENTLY</c> builds a day's index while records keep arriving and searches keep running, and
/// attaching it to the parent index only records that it exists. A build that fails leaves an invalid index, which the
/// next attempt drops and builds again. Until a day is done it stays on the list, and searches and counts match words
/// on it the old way.
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class LogWordIndexBackfill(NpgsqlDataSource dataSource)
{
    // Dropping the old index needs a short exclusive lock on every day. Waiting behind a long search would stop the
    // writer behind it, so the drop gives up quickly and is tried again a minute later.
    private const string _dropOldIndex =
        """
        SET lock_timeout = '2s';
        DROP INDEX IF EXISTS storage.log_record_body_text;
        RESET lock_timeout;
        """;

    /// <summary>Builds the index on the newest day left, or drops the old index when no day is left.</summary>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>Whether a day was built; <see langword="false"/> when none is left.</returns>
    public async Task<bool> BuildNextDayAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var day = await NewestDayAsync(connection, cancellationToken);

        if (day is null)
        {
            await DropOldIndexAsync(connection, cancellationToken);

            return false;
        }

        // The names are built from a date this method formatted itself, so they are safe to inline.
        var partition = "log_record_" + day.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var index = partition + "_terms";

        if (await ExistsAsync(connection, partition, cancellationToken))
        {
            await BuildAsync(connection, partition, index, cancellationToken);
        }

        // A day dropped by retention meanwhile has nothing left to build.
        await using var done = new NpgsqlCommand("DELETE FROM storage.log_terms_backfill WHERE day = $1", connection);
        done.Parameters.AddWithValue(day.Value);
        await done.ExecuteNonQueryAsync(cancellationToken);

        return true;
    }

    private static async Task<DateOnly?> NewestDayAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT max(day) FROM storage.log_terms_backfill", connection);
        var value = await command.ExecuteScalarAsync(token);

        return value is DateOnly day ? day : null;
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string table, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('storage.' || $1) IS NOT NULL", connection);
        command.Parameters.AddWithValue(table);

        return (bool)(await command.ExecuteScalarAsync(token))!;
    }

    private static async Task BuildAsync(
        NpgsqlConnection connection,
        string partition,
        string index,
        CancellationToken token)
    {
        // An index left invalid by a build that failed must go before it can be built again.
        await using (var invalid = new NpgsqlCommand(
            "SELECT NOT indisvalid FROM pg_index WHERE indexrelid = to_regclass('storage.' || $1)",
            connection))
        {
            invalid.Parameters.AddWithValue(index);

            if (await invalid.ExecuteScalarAsync(token) is true)
            {
                await ExecuteAsync(connection, $"DROP INDEX CONCURRENTLY storage.{index}", token);
            }
        }

        await ExecuteAsync(
            connection,
            $"CREATE INDEX CONCURRENTLY IF NOT EXISTS {index} ON storage.{partition} "
            + "USING gin (storage.log_terms(body, timestamp))",
            token);

        await using var attached = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_inherits WHERE inhrelid = to_regclass('storage.' || $1))",
            connection);
        attached.Parameters.AddWithValue(index);

        if (await attached.ExecuteScalarAsync(token) is false)
        {
            await ExecuteAsync(
                connection,
                $"ALTER INDEX storage.log_record_terms ATTACH PARTITION storage.{index}",
                token);
        }
    }

    private static async Task DropOldIndexAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var exists = new NpgsqlCommand(
            "SELECT to_regclass('storage.log_record_body_text') IS NOT NULL",
            connection);

        if (await exists.ExecuteScalarAsync(token) is true)
        {
            await ExecuteAsync(connection, _dropOldIndex, token);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        // A busy day takes a minute or more to index, far beyond the default 30 seconds. A timeout would cancel the
        // build and leave it invalid, and every retry would time out the same way. Stopping Veyra still cancels it.
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(token);
    }
}
