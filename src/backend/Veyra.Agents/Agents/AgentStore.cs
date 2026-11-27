using Npgsql;
using NpgsqlTypes;

namespace Veyra.Agents.Agents;

/// <summary>
/// Reads and writes agents in <c>agents.agent</c>. Revoked agents stay in the table but are never returned.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class AgentStore(NpgsqlDataSource dataSource)
{
    private const string _columns =
        "id, name, placement, ingestion_key_id, created_at, instance_uid, version, host_name, os, healthy, " +
        "health_error, config_hash, config_state, config_error, last_seen_at, watch_host, watch_event_log, " +
        "watch_iis, watch_environment, watch_docker, health_started_at";

    /// <summary>Stores a new agent.</summary>
    /// <param name="agent">The agent. Its report is not stored; a new agent has none.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the agent is stored.</returns>
    public async Task InsertAsync(Agent agent, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO agents.agent (id, name, placement, ingestion_key_id, created_at)
            VALUES ($1, $2, $3, $4, $5)
            """);

        command.Parameters.AddWithValue(agent.Id);
        command.Parameters.AddWithValue(agent.Name);
        command.Parameters.AddWithValue((short)agent.Placement);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)agent.IngestionKeyId ?? DBNull.Value);
        command.Parameters.AddWithValue(agent.CreatedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Finds the active agent that signs in with a key.</summary>
    /// <param name="ingestionKeyId">The ID of a valid ingestion key.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The agent, or <see langword="null"/> when the key belongs to no active agent.</returns>
    public async Task<Agent?> FindByKeyAsync(Guid ingestionKeyId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM agents.agent WHERE ingestion_key_id = $1 AND revoked_at IS NULL");
        command.Parameters.AddWithValue(ingestionKeyId);

        return (await ReadAllAsync(command, cancellationToken)).SingleOrDefault();
    }

    /// <summary>Finds an active agent by its ID.</summary>
    /// <param name="id">The agent's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The agent, or <see langword="null"/> when there is no active agent with that ID.</returns>
    public async Task<Agent?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM agents.agent WHERE id = $1 AND revoked_at IS NULL");
        command.Parameters.AddWithValue(id);

        return (await ReadAllAsync(command, cancellationToken)).SingleOrDefault();
    }

    /// <summary>Finds the active built-in agent.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The agent, or <see langword="null"/> when Veyra has not set one up.</returns>
    public async Task<Agent?> FindBuiltInAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM agents.agent WHERE placement = $1 AND revoked_at IS NULL");
        command.Parameters.AddWithValue((short)AgentPlacement.BuiltIn);

        return (await ReadAllAsync(command, cancellationToken)).SingleOrDefault();
    }

    /// <summary>Lists the active agents, the built-in one first, then by name.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The agents.</returns>
    public async Task<IReadOnlyList<Agent>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM agents.agent WHERE revoked_at IS NULL ORDER BY placement, name, created_at");

        return await ReadAllAsync(command, cancellationToken);
    }

    /// <summary>Gives an agent a new ingestion key, when the old one is lost or revoked.</summary>
    /// <param name="id">The agent's ID.</param>
    /// <param name="ingestionKeyId">The new key's ID.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the key is replaced.</returns>
    public async Task ReplaceKeyAsync(Guid id, Guid ingestionKeyId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE agents.agent SET ingestion_key_id = $2 WHERE id = $1");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(ingestionKeyId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Stores what an agent last reported, replacing the previous report.</summary>
    /// <param name="id">The agent's ID.</param>
    /// <param name="report">The merged report.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the report is stored.</returns>
    public async Task SaveReportAsync(Guid id, AgentReport report, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agents.agent
            SET instance_uid = $2, version = $3, host_name = $4, os = $5, healthy = $6, health_error = $7,
                config_hash = $8, config_state = $9, config_error = $10, last_seen_at = $11, health_started_at = $12
            WHERE id = $1
            """);

        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(NpgsqlDbType.Bytea, (object?)report.InstanceUid ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)report.Version ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)report.HostName ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)report.OperatingSystem ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Boolean, (object?)report.Healthy ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)report.HealthError ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Bytea, (object?)report.ConfigHash ?? DBNull.Value);
        command.Parameters.AddWithValue((short)report.ConfigState);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)report.ConfigError ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)report.LastSeenAt ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)report.StartedAt ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Stores what an agent reads of its host.</summary>
    /// <param name="id">The agent's ID.</param>
    /// <param name="watch">The setting, already checked.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the setting is stored.</returns>
    public async Task SaveWatchAsync(Guid id, HostWatch watch, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agents.agent
            SET watch_host = $2, watch_event_log = $3, watch_iis = $4, watch_environment = $5, watch_docker = $6
            WHERE id = $1
            """);

        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(watch.Host);
        command.Parameters.AddWithValue(watch.EventLog);
        command.Parameters.AddWithValue(watch.Iis);
        command.Parameters.AddWithValue(watch.Environment);
        command.Parameters.AddWithValue(watch.Docker);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Counts the database servers an agent reads, SQL Server and PostgreSQL.</summary>
    /// <param name="id">The agent's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of servers.</returns>
    public async Task<int> CountServersAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT ((SELECT count(*) FROM agents.sql_server WHERE agent_id = $1)
                  + (SELECT count(*) FROM agents.postgres_server WHERE agent_id = $1))::int
            """);
        command.Parameters.AddWithValue(id);

        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Revokes an agent. Its key must be revoked too, by the caller.</summary>
    /// <param name="id">The agent's ID.</param>
    /// <param name="revokedAt">When it was revoked.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the agent is revoked.</returns>
    public async Task RevokeAsync(Guid id, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE agents.agent SET revoked_at = $2 WHERE id = $1 AND revoked_at IS NULL");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(revokedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<Agent>> ReadAllAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var agents = new List<Agent>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var report = new AgentReport(
                InstanceUid: reader.IsDBNull(5) ? null : reader.GetFieldValue<byte[]>(5),
                Version: reader.IsDBNull(6) ? null : reader.GetString(6),
                HostName: reader.IsDBNull(7) ? null : reader.GetString(7),
                OperatingSystem: reader.IsDBNull(8) ? null : reader.GetString(8),
                Healthy: reader.IsDBNull(9) ? null : reader.GetBoolean(9),
                HealthError: reader.IsDBNull(10) ? null : reader.GetString(10),
                ConfigHash: reader.IsDBNull(11) ? null : reader.GetFieldValue<byte[]>(11),
                ConfigState: (AgentConfigState)reader.GetInt16(12),
                ConfigError: reader.IsDBNull(13) ? null : reader.GetString(13),
                LastSeenAt: reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
                StartedAt: reader.IsDBNull(20) ? null : reader.GetFieldValue<DateTimeOffset>(20));

            agents.Add(new Agent(
                Id: reader.GetGuid(0),
                Name: reader.GetString(1),
                Placement: (AgentPlacement)reader.GetInt16(2),
                IngestionKeyId: reader.IsDBNull(3) ? null : reader.GetGuid(3),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(4),
                Report: report,
                Watch: new HostWatch(
                    Host: reader.GetBoolean(15),
                    EventLog: reader.GetBoolean(16),
                    Iis: reader.GetBoolean(17),
                    Environment: reader.GetString(18),
                    Docker: reader.GetBoolean(19))));
        }

        return agents;
    }
}
