using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Probes;

/// <summary>Reads and writes connection probes in <c>agents.sql_probe</c> (ADR 0028).</summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class ProbeStore(NpgsqlDataSource dataSource)
{
    private const string _columns =
        "id, agent_id, server_id, platform, address, port, instance, authentication, login, encryption, " +
        "database_name, started_at, finished_at, state, reason, message, result";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Stores a new probe.</summary>
    /// <param name="probe">The probe.</param>
    /// <param name="protectedPassword">Its encrypted password, or <see langword="null"/> for a finished one.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the probe is stored.</returns>
    public async Task InsertAsync(SqlProbe probe, string? protectedPassword, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var connection = probe.Connection;

        await using var command = dataSource.CreateCommand(
            $"""
            INSERT INTO agents.sql_probe ({_columns}, password)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18)
            """);
        command.Parameters.AddWithValue(probe.Id);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)probe.AgentId ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)probe.ServerId ?? DBNull.Value);
        command.Parameters.AddWithValue((short)connection.Platform);
        command.Parameters.AddWithValue(connection.Address);
        command.Parameters.AddWithValue(connection.Port);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)connection.Instance ?? DBNull.Value);
        command.Parameters.AddWithValue((short)connection.Authentication);
        command.Parameters.AddWithValue(connection.Login);
        command.Parameters.AddWithValue((short)connection.Encryption);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)connection.Database ?? DBNull.Value);
        command.Parameters.AddWithValue(probe.StartedAt);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)probe.FinishedAt ?? DBNull.Value);
        command.Parameters.AddWithValue((short)probe.State);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Smallint,
            probe.Failure is { } failure ? (short)failure : DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)probe.Message ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, Json(probe.Result));
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)protectedPassword ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Finds a probe.</summary>
    /// <param name="id">The probe's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The probe, or <see langword="null"/> when there is none, or it was purged.</returns>
    public async Task<SqlProbe?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns}, password FROM agents.sql_probe WHERE id = $1");
        command.Parameters.AddWithValue(id);

        return (await ReadAllAsync(command, cancellationToken)).Select(entry => entry.Probe).SingleOrDefault();
    }

    /// <summary>Finds a saved server's latest probe.</summary>
    /// <param name="serverId">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The probe, or <see langword="null"/> when it has had none lately.</returns>
    public async Task<SqlProbe?> LatestForServerAsync(Guid serverId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {_columns}, password FROM agents.sql_probe
            WHERE server_id = $1
            ORDER BY started_at DESC, id DESC
            LIMIT 1
            """);
        command.Parameters.AddWithValue(serverId);

        return (await ReadAllAsync(command, cancellationToken)).Select(entry => entry.Probe).SingleOrDefault();
    }

    /// <summary>Lists the pending probes, oldest first, with their encrypted passwords.</summary>
    /// <param name="agentId">Only those one agent runs; <see langword="null"/> for every agent's.</param>
    /// <param name="startedSince">Only those asked for after this time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The probes.</returns>
    public async Task<IReadOnlyList<(SqlProbe Probe, string? ProtectedPassword)>> ListPendingAsync(
        Guid? agentId,
        DateTimeOffset startedSince,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {_columns}, password FROM agents.sql_probe
            WHERE state = 0 AND ($1::uuid IS NULL OR agent_id = $1) AND started_at > $2
            ORDER BY started_at, id
            """);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue(startedSince);

        return await ReadAllAsync(command, cancellationToken);
    }

    /// <summary>Lists every pending probe, oldest first, without passwords.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The probes.</returns>
    public async Task<IReadOnlyList<SqlProbe>> ListAllPendingAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns}, NULL FROM agents.sql_probe WHERE state = 0 ORDER BY started_at, id");

        return (await ReadAllAsync(command, cancellationToken)).Select(entry => entry.Probe).ToList();
    }

    /// <summary>Finishes a pending probe and forgets its password.</summary>
    /// <param name="id">The probe's ID.</param>
    /// <param name="state">How it finished.</param>
    /// <param name="failure">Why it failed, for a failed one.</param>
    /// <param name="message">The error in words, for a failed one.</param>
    /// <param name="result">What it read, for one that succeeded.</param>
    /// <param name="finishedAt">When it finished.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>Whether it was still pending; an answer that arrives twice finds it finished.</returns>
    public async Task<bool> FinishAsync(
        Guid id,
        ProbeState state,
        ProbeFailure? failure,
        string? message,
        ProbeResult? result,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agents.sql_probe
            SET state = $2, reason = $3, message = $4, result = $5, finished_at = $6, password = NULL
            WHERE id = $1 AND state = 0
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue((short)state);
        command.Parameters.AddWithValue(NpgsqlDbType.Smallint, failure is { } reason ? (short)reason : DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)message ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, Json(result));
        command.Parameters.AddWithValue(finishedAt);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Deletes the probes asked for before a time, whatever their state.</summary>
    /// <param name="cutoff">Probes started before this are deleted.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How many were deleted.</returns>
    public async Task<int> DeleteStartedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM agents.sql_probe WHERE started_at < $1");
        command.Parameters.AddWithValue(cutoff);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object Json(ProbeResult? result) =>
        result is null ? DBNull.Value : JsonSerializer.Serialize(result, _json);

    private static async Task<IReadOnlyList<(SqlProbe Probe, string? ProtectedPassword)>> ReadAllAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var probes = new List<(SqlProbe, string?)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var connection = new SqlServerConnection(
                Platform: (SqlServerPlatform)reader.GetInt16(3),
                Address: reader.GetString(4),
                Port: reader.GetInt32(5),
                Instance: reader.IsDBNull(6) ? null : reader.GetString(6),
                Authentication: (SqlServerAuthentication)reader.GetInt16(7),
                Login: reader.GetString(8),
                Encryption: (SqlServerEncryption)reader.GetInt16(9),
                Database: reader.IsDBNull(10) ? null : reader.GetString(10));

            var probe = new SqlProbe(
                Id: reader.GetGuid(0),
                AgentId: reader.IsDBNull(1) ? null : reader.GetGuid(1),
                ServerId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                Connection: connection,
                StartedAt: reader.GetFieldValue<DateTimeOffset>(11),
                FinishedAt: reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                State: (ProbeState)reader.GetInt16(13),
                Failure: reader.IsDBNull(14) ? null : (ProbeFailure)reader.GetInt16(14),
                Message: reader.IsDBNull(15) ? null : reader.GetString(15),
                Result: reader.IsDBNull(16)
                    ? null
                    : JsonSerializer.Deserialize<ProbeResult>(reader.GetString(16), _json));

            probes.Add((probe, reader.IsDBNull(17) ? null : reader.GetString(17)));
        }

        return probes;
    }
}
