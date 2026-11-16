using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Veyra.Core.Activity;

namespace Veyra.Storage.Activity;

/// <summary>
/// The store for database activity (ADR 0020): what Veyra's agents read of each database server with queries of their
/// own, kept in tables of its own instead of as log records or metric series.
/// </summary>
/// <remarks>
/// <para>
/// A read of a server's live view is one row per busy session plus session 0, which holds the server-wide figures. A
/// read without busy sessions is session 0 alone, so the newest read always says what is busy now, even when nothing
/// is.
/// </para>
/// <para>
/// Writes use binary <c>COPY</c>; a read finds the newest read time from session 0 on its own small index first, then
/// that read's rows, so it stays fast on servers with many busy sessions.
/// </para>
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class ActivityStore(NpgsqlDataSource dataSource) : IDatabaseActivityWriter, IDatabaseActivitySearch
{
    private const string _sessionColumns =
        "block_name, environment, sampled_at, session_id, request_status, session_status, command, database_name, "
        + "program, host, login, blocked_by, wait_type, wait_ms, wait_resource, elapsed_ms, cpu_ms, reads, writes, "
        + "tempdb_kb, grant_kb, grant_requested_kb, grant_waiting, transaction_ms, transaction_log_records, "
        + "open_transactions, isolation, percent_complete, remaining_ms, query_hash, plan_hash, idle_ms, "
        + "connected_seconds, text, running, blocking, tempdb_size_kb, tempdb_free_kb, grants_waiting";

    /// <inheritdoc />
    public async Task WriteAsync(IReadOnlyList<DatabaseActivity> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = records.OfType<ActivityRecord>().ToList();

        if (rows.Count > 0)
        {
            await WriteRecordsAsync(connection, rows, cancellationToken);
        }

        if (rows.Count == records.Count)
        {
            return;
        }

        await using var writer = await connection.BeginBinaryImportAsync(
            $"COPY storage.{ActivityTables.Sessions} ({_sessionColumns}) FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        foreach (var record in records)
        {
            switch (record)
            {
                case SessionSample session:
                    await WriteSessionAsync(writer, session, cancellationToken);
                    break;
                case ServerSample server:
                    await WriteServerAsync(writer, server, cancellationToken);
                    break;
                case ActivityRecord:
                    break;
                default:
                    throw new ArgumentException($"The store cannot keep {record.GetType().Name}.", nameof(records));
            }
        }

        await writer.CompleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SessionSnapshot?> GetSessionsAsync(
        string block,
        DateTimeOffset at,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(block);

        await using var latest = dataSource.CreateCommand(
            $"""
            SELECT max(sampled_at) FROM storage.{ActivityTables.Sessions}
            WHERE block_name = $1 AND session_id = 0 AND sampled_at <= $2 AND sampled_at > $3
            """);
        latest.Parameters.AddWithValue(block);
        latest.Parameters.AddWithValue(at);
        latest.Parameters.AddWithValue(at - maxAge);

        if (await latest.ExecuteScalarAsync(cancellationToken) is not DateTime sampledAt)
        {
            return null;
        }

        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {_sessionColumns} FROM storage.{ActivityTables.Sessions}
            WHERE block_name = $1 AND sampled_at = $2
            """);
        command.Parameters.AddWithValue(block);
        command.Parameters.AddWithValue(new DateTimeOffset(sampledAt, TimeSpan.Zero));

        var snapshots = await ReadSnapshotsAsync(command, cancellationToken);

        return snapshots.Count == 0 ? null : snapshots[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSnapshot>> ListLatestSessionsAsync(
        DateTimeOffset now,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            WITH latest AS (
                SELECT DISTINCT ON (block_name, environment) block_name, environment, sampled_at
                FROM storage.{ActivityTables.Sessions}
                WHERE session_id = 0 AND sampled_at <= $1 AND sampled_at > $2
                ORDER BY block_name, environment, sampled_at DESC
            )
            SELECT {string.Join(", ", _sessionColumns.Split(", ").Select(column => "s." + column))}
            FROM latest
            JOIN storage.{ActivityTables.Sessions} AS s
              ON s.block_name = latest.block_name
             AND s.sampled_at = latest.sampled_at
             AND s.environment IS NOT DISTINCT FROM latest.environment
            """);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(now - maxAge);

        return await ReadSnapshotsAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActivityRecord>> ListRecordsAsync(
        string block,
        string kind,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        IReadOnlyCollection<string>? keys,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT block_name, environment, sampled_at, kind, key, attributes::text, body
            FROM storage.{ActivityTables.Records}
            WHERE block_name = $1 AND kind = $2 AND sampled_at >= $3 AND sampled_at <= $4
              AND ($5::text[] IS NULL OR key = ANY($5))
            ORDER BY sampled_at DESC
            LIMIT $6
            """);
        command.Parameters.AddWithValue(block);
        command.Parameters.AddWithValue(kind);
        command.Parameters.AddWithValue(periodStart);
        command.Parameters.AddWithValue(periodEnd);
        command.Parameters.Add(
            new NpgsqlParameter
            {
                Value = keys is null ? DBNull.Value : keys.ToArray(),
                NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            });
        command.Parameters.AddWithValue(limit);

        var records = new List<ActivityRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ActivityRecord(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero),
                reader.GetString(3),
                reader.GetString(4),
                JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5)) ?? [],
                reader.GetString(6)));
        }

        return records;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSample>> ListBlockingAsync(
        string block,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {_sessionColumns} FROM storage.{ActivityTables.Sessions}
            WHERE block_name = $1 AND sampled_at >= $2 AND sampled_at <= $3 AND session_id <> 0
              AND (blocked_by > 0 OR blocking)
            ORDER BY sampled_at
            LIMIT $4
            """);
        command.Parameters.AddWithValue(block);
        command.Parameters.AddWithValue(periodStart);
        command.Parameters.AddWithValue(periodEnd);
        command.Parameters.AddWithValue(limit);

        var sessions = new List<SessionSample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(Session(reader));
        }

        return sessions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionCaller>> ListCallersAsync(
        string block,
        string queryHash,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT program, host, login, count(*)
            FROM storage.{ActivityTables.Sessions}
            WHERE block_name = $1 AND query_hash = $2 AND sampled_at >= $3 AND sampled_at <= $4
            GROUP BY program, host, login
            ORDER BY count(*) DESC
            LIMIT 20
            """);
        command.Parameters.AddWithValue(block);
        command.Parameters.AddWithValue(queryHash);
        command.Parameters.AddWithValue(periodStart);
        command.Parameters.AddWithValue(periodEnd);

        var callers = new List<SessionCaller>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            callers.Add(
                new SessionCaller(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3)));
        }

        return callers;
    }

    // Records the agent sends again and again, such as the plan of a busy statement every 15 minutes, are kept once a
    // day per key; the rest are kept as they come.
    private static async Task WriteRecordsAsync(
        NpgsqlConnection connection,
        List<ActivityRecord> records,
        CancellationToken cancellationToken)
    {
        await using var batch = new NpgsqlBatch(connection);

        foreach (var record in records)
        {
            var once = OncePerDayKinds.Contains(record.Kind) && record.Key.Length > 0;
            var command = new NpgsqlBatchCommand(
                $"""
                INSERT INTO storage.{ActivityTables.Records}
                    (block_name, environment, sampled_at, kind, key, attributes, body)
                SELECT $1, $2, $3, $4, $5, $6::jsonb, $7
                WHERE NOT $8 OR NOT EXISTS (
                    SELECT 1 FROM storage.{ActivityTables.Records}
                    WHERE block_name = $1 AND kind = $4 AND key = $5 AND sampled_at > $3 - interval '1 day')
                """);
            command.Parameters.AddWithValue(record.Block);
            command.Parameters.Add(
                new NpgsqlParameter
                {
                    Value = (object?)record.Environment ?? DBNull.Value,
                    NpgsqlDbType = NpgsqlDbType.Text,
                });
            command.Parameters.AddWithValue(record.SampledAt);
            command.Parameters.AddWithValue(record.Kind);
            command.Parameters.AddWithValue(record.Key);
            command.Parameters.AddWithValue(JsonSerializer.Serialize(record.Attributes));
            command.Parameters.AddWithValue(record.Body);
            command.Parameters.AddWithValue(once);
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The kinds of record kept once a day per key, because the agent sends them again and again.</summary>
    public static readonly IReadOnlySet<string> OncePerDayKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "sqlserver.plan",
        "sqlserver.deadlock",
        "sqlserver.event",
        "sqlserver.querystore",
        "sqlserver.index",
        "sqlserver.snapshot",
        "sqlserver.job",
    };

    private static async Task<IReadOnlyList<SessionSnapshot>> ReadSnapshotsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var servers = new Dictionary<(string, string?, DateTimeOffset), ServerSample>();
        var sessions = new Dictionary<(string, string?, DateTimeOffset), List<SessionSample>>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var block = reader.GetString(0);
            var environment = reader.IsDBNull(1) ? null : reader.GetString(1);
            var sampledAt = new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero);
            var key = (block, environment, sampledAt);
            var sessionId = reader.GetInt32(3);

            if (sessionId == 0)
            {
                servers[key] = new ServerSample(
                    block,
                    environment,
                    sampledAt,
                    TempdbSizeKilobytes: reader.GetInt64(36),
                    TempdbFreeKilobytes: reader.GetInt64(37),
                    GrantsWaiting: reader.GetInt32(38));

                continue;
            }

            if (!sessions.TryGetValue(key, out var list))
            {
                sessions[key] = list = [];
            }

            // A request the agent sent twice holds the same session of the same read twice; it counts once.
            list.RemoveAll(earlier => earlier.SessionId == sessionId);
            list.Add(Session(reader));
        }

        // A read without its session 0 is incomplete, written while it was being read; it is left out.
        return servers
            .Select(pair => new SessionSnapshot(
                pair.Key.Item1,
                pair.Key.Item2,
                pair.Key.Item3,
                pair.Value,
                sessions.TryGetValue(pair.Key, out var list) ? list : []))
            .ToList();
    }

    private static SessionSample Session(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero),
            SessionId: reader.GetInt32(3),
            RequestStatus: reader.GetString(4),
            SessionStatus: reader.GetString(5),
            Command: reader.GetString(6),
            Database: reader.GetString(7),
            Program: reader.GetString(8),
            Host: reader.GetString(9),
            Login: reader.GetString(10),
            BlockedBy: reader.GetInt32(11),
            WaitType: reader.GetString(12),
            WaitMilliseconds: reader.GetInt64(13),
            WaitResource: reader.GetString(14),
            ElapsedMilliseconds: reader.GetInt64(15),
            CpuMilliseconds: reader.GetInt64(16),
            Reads: reader.GetInt64(17),
            Writes: reader.GetInt64(18),
            TempdbKilobytes: reader.GetInt64(19),
            GrantKilobytes: reader.GetInt64(20),
            GrantRequestedKilobytes: reader.GetInt64(21),
            GrantWaiting: reader.GetBoolean(22),
            TransactionMilliseconds: reader.IsDBNull(23) ? null : reader.GetInt64(23),
            TransactionLogRecords: reader.GetInt64(24),
            OpenTransactions: reader.GetInt32(25),
            Isolation: reader.GetInt16(26),
            PercentComplete: reader.GetFloat(27),
            RemainingMilliseconds: reader.GetInt64(28),
            QueryHash: reader.GetString(29),
            PlanHash: reader.GetString(30),
            IdleMilliseconds: reader.GetInt64(31),
            ConnectedSeconds: reader.GetInt64(32),
            Text: reader.GetString(33),
            Running: reader.GetBoolean(34),
            Blocking: reader.GetBoolean(35));

    private static async Task WriteSessionAsync(
        NpgsqlBinaryImporter writer,
        SessionSample session,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await WriteKeyAsync(writer, session, session.SessionId, cancellationToken);
        await writer.WriteAsync(session.RequestStatus, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.SessionStatus, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.Command, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.Database, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.Program, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.Host, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.Login, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.BlockedBy, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(session.WaitType, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.WaitMilliseconds, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.WaitResource, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.ElapsedMilliseconds, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.CpuMilliseconds, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.Reads, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.Writes, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.TempdbKilobytes, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.GrantKilobytes, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.GrantRequestedKilobytes, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.GrantWaiting, NpgsqlDbType.Boolean, cancellationToken);

        if (session.TransactionMilliseconds is { } transaction)
        {
            await writer.WriteAsync(transaction, NpgsqlDbType.Bigint, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }

        await writer.WriteAsync(session.TransactionLogRecords, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.OpenTransactions, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync((short)session.Isolation, NpgsqlDbType.Smallint, cancellationToken);
        await writer.WriteAsync((float)session.PercentComplete, NpgsqlDbType.Real, cancellationToken);
        await writer.WriteAsync(session.RemainingMilliseconds, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.QueryHash, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.PlanHash, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.IdleMilliseconds, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.ConnectedSeconds, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(session.Text, NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(session.Running, NpgsqlDbType.Boolean, cancellationToken);
        await writer.WriteAsync(session.Blocking, NpgsqlDbType.Boolean, cancellationToken);
        await writer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
    }

    private static async Task WriteServerAsync(
        NpgsqlBinaryImporter writer,
        ServerSample server,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await WriteKeyAsync(writer, server, 0, cancellationToken);

        // Session 0 has no session's columns: empty texts, zeros, no transaction.
        for (var text = 0; text < 7; text++)
        {
            await writer.WriteAsync("", NpgsqlDbType.Text, cancellationToken);
        }

        await writer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync("", NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync("", NpgsqlDbType.Text, cancellationToken);

        for (var number = 0; number < 7; number++)
        {
            await writer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
        }

        await writer.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
        await writer.WriteNullAsync(cancellationToken);
        await writer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync((short)0, NpgsqlDbType.Smallint, cancellationToken);
        await writer.WriteAsync(0f, NpgsqlDbType.Real, cancellationToken);
        await writer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync("", NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync("", NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync("", NpgsqlDbType.Text, cancellationToken);
        await writer.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
        await writer.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
        await writer.WriteAsync(server.TempdbSizeKilobytes, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(server.TempdbFreeKilobytes, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(server.GrantsWaiting, NpgsqlDbType.Integer, cancellationToken);
    }

    private static async Task WriteKeyAsync(
        NpgsqlBinaryImporter writer,
        DatabaseActivity record,
        int sessionId,
        CancellationToken cancellationToken)
    {
        await writer.WriteAsync(record.Block, NpgsqlDbType.Text, cancellationToken);

        if (record.Environment is null)
        {
            await writer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await writer.WriteAsync(record.Environment, NpgsqlDbType.Text, cancellationToken);
        }

        await writer.WriteAsync(record.SampledAt, NpgsqlDbType.TimestampTz, cancellationToken);
        await writer.WriteAsync(sessionId, NpgsqlDbType.Integer, cancellationToken);
    }
}
