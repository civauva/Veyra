using Npgsql;
using NpgsqlTypes;
using Veyra.Core.Alerts;
using Veyra.Core.Search;

namespace Veyra.Storage.Search;

/// <summary>
/// Finds names for the search in the top bar (ADR 0022): blocks in the metric series and the hourly log counts, and
/// root operations in the root-span index.
/// </summary>
/// <remarks>
/// Every read is bounded by the window. The series table holds one row per series, a few thousand on a busy
/// installation; the hourly counts a few rows per service and hour; root operations are read from the root-span index
/// alone. Matching ignores case and treats the text literally: <c>%</c> and <c>_</c> are not wildcards.
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class SignalSearchStore(NpgsqlDataSource dataSource) : ISignalSearch
{
    // $1 from, $2 to, $3 kinds, $4 environment or null, $5 the text escaped for LIKE, $6 the text, $7 the limit.
    // A host, container, or database server that names no environment belongs to every one; a service does not.
    // A database server is shown by its server, the second word of its block name (ADR 0018).
    private const string _blocks =
        """
        WITH named AS (
            SELECT block_kind, block_name, NULL::text AS database, 0::bigint AS records
            FROM storage.metric_series
            WHERE block_kind = ANY($3) AND last_seen >= $1 AND first_seen < $2
              AND ($4::text IS NULL OR environment = $4 OR (block_kind <> 1 AND environment IS NULL))
            GROUP BY block_kind, block_name
            UNION ALL
            SELECT block_kind, block_name,
                   coalesce(attributes ->> 'database_name', attributes ->> 'postgresql.database.name'), 0
            FROM storage.metric_series
            WHERE block_kind = 3 AND 3 = ANY($3) AND last_seen >= $1 AND first_seen < $2
              AND (attributes ? 'database_name' OR attributes ? 'postgresql.database.name')
              AND ($4::text IS NULL OR environment = $4 OR environment IS NULL)
            GROUP BY 1, 2, 3
            UNION ALL
            SELECT block_kind, service_name, NULL, sum(records)::bigint
            FROM storage.log_count_hour
            WHERE block_kind = ANY($3) AND hour >= date_trunc('hour', $1) AND hour < $2
              AND ($4::text IS NULL OR environment = $4 OR (block_kind <> 1 AND environment IS NULL))
            GROUP BY block_kind, service_name
        ),
        shown AS (
            SELECT block_kind, block_name, database, records,
                   lower(coalesce(
                       database,
                       CASE WHEN block_kind = 3 THEN split_part(block_name, ' ', 2) ELSE block_name END)) AS name
            FROM named
        )
        SELECT block_kind, block_name, database, sum(records)::bigint
        FROM shown
        WHERE name LIKE '%' || lower($5) || '%' ESCAPE '\'
        GROUP BY block_kind, block_name, database, name
        ORDER BY CASE WHEN name = lower($6) THEN 0 WHEN name LIKE lower($5) || '%' ESCAPE '\' THEN 1 ELSE 2 END,
                 name, block_kind, block_name, database
        LIMIT $7
        """;

    // $1 from, $2 to, $3 the text escaped for LIKE, $4 environment or null, $5 the text, $6 the limit.
    private const string _operations =
        """
        SELECT name, service_name, count(*), count(*) FILTER (WHERE status = 2)
        FROM storage.span
        WHERE parent_span_id IS NULL AND start_time >= $1 AND start_time < $2
          AND lower(name) LIKE '%' || lower($3) || '%' ESCAPE '\'
          AND ($4::text IS NULL OR environment = $4)
        GROUP BY name, service_name
        ORDER BY CASE WHEN lower(name) = lower($5) THEN 0 WHEN lower(name) LIKE lower($3) || '%' ESCAPE '\' THEN 1
                      ELSE 2 END,
                 lower(name), name, service_name
        LIMIT $6
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FoundBlock>> FindBlocksAsync(
        NameSearch search,
        IReadOnlyList<AlertBlockKind> kinds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(kinds);

        await using var command = dataSource.CreateCommand(_blocks);
        command.Parameters.AddWithValue(search.From.ToUniversalTime());
        command.Parameters.AddWithValue(search.To.ToUniversalTime());
        command.Parameters.AddWithValue(kinds.Select(kind => (short)kind).ToArray());
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = EnvironmentOf(search) });
        command.Parameters.AddWithValue(Escape(search.Text));
        command.Parameters.AddWithValue(search.Text);
        command.Parameters.AddWithValue(search.Limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var found = new List<FoundBlock>();

        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(new FoundBlock(
                (AlertBlockKind)reader.GetInt16(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt64(3)));
        }

        return found;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FoundOperation>> FindOperationsAsync(
        NameSearch search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        await using var command = dataSource.CreateCommand(_operations);
        command.Parameters.AddWithValue(search.From.ToUniversalTime());
        command.Parameters.AddWithValue(search.To.ToUniversalTime());
        command.Parameters.AddWithValue(Escape(search.Text));
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = EnvironmentOf(search) });
        command.Parameters.AddWithValue(search.Text);
        command.Parameters.AddWithValue(search.Limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var found = new List<FoundOperation>();

        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(new FoundOperation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return found;
    }

    /// <summary>Escapes the text for <c>LIKE … ESCAPE '\'</c>, so it is matched literally.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with <c>\</c>, <c>%</c>, and <c>_</c> escaped.</returns>
    internal static string Escape(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    private static object EnvironmentOf(NameSearch search) => (object?)search.Environment ?? DBNull.Value;
}
