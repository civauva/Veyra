using Npgsql;

namespace Veyra.Agents.Enrollment;

/// <summary>Reads and writes one-time codes in <c>agents.enrollment</c>, by their hashes only.</summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class EnrollmentStore(NpgsqlDataSource dataSource)
{
    /// <summary>Stores a new code.</summary>
    /// <param name="agentId">The agent it installs.</param>
    /// <param name="hash">The code's hash.</param>
    /// <param name="createdAt">When it was made.</param>
    /// <param name="expiresAt">When it stops working.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the code is stored.</returns>
    public async Task InsertAsync(
        Guid agentId,
        byte[] hash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO agents.enrollment (code_hash, agent_id, created_at, expires_at) VALUES ($1, $2, $3, $4)");
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(agentId);
        command.Parameters.AddWithValue(createdAt);
        command.Parameters.AddWithValue(expiresAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Uses a code: marks it used and returns its agent, in one statement, so two installers can never both use it.
    /// </summary>
    /// <param name="hash">The code's hash.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>
    /// The agent's ID, or <see langword="null"/> when no code has the hash, it was used, or it expired.
    /// </returns>
    public async Task<Guid?> UseAsync(byte[] hash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agents.enrollment SET used_at = $2
            WHERE code_hash = $1 AND used_at IS NULL AND expires_at > $2
            RETURNING agent_id
            """);
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(now);

        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    /// <summary>When each agent's newest unused code stops working, for the Agents page.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Each agent with a code still open, and when it expires.</returns>
    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> OpenCodesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT agent_id, max(expires_at) FROM agents.enrollment
            WHERE used_at IS NULL AND expires_at > $1
            GROUP BY agent_id
            """);
        command.Parameters.AddWithValue(now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var open = new Dictionary<Guid, DateTimeOffset>();

        while (await reader.ReadAsync(cancellationToken))
        {
            open[reader.GetGuid(0)] = reader.GetFieldValue<DateTimeOffset>(1);
        }

        return open;
    }

    /// <summary>Removes every code of an agent, used or not, so none of them works any more.</summary>
    /// <param name="agentId">The agent.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>A task that completes when the codes are gone.</returns>
    public async Task DeleteForAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM agents.enrollment WHERE agent_id = $1");
        command.Parameters.AddWithValue(agentId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
