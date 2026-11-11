using Npgsql;

namespace Veyra.Access.SecondFactor;

/// <summary>
/// Reads and writes second factors and recovery codes in <c>access.user_second_factor</c> and
/// <c>access.recovery_code</c> (ADR 0011).
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class SecondFactorStore(NpgsqlDataSource dataSource)
{
    /// <summary>Finds a person's second factor, switched on or still being set up.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The second factor, or <see langword="null"/> when there is none.</returns>
    public async Task<SecondFactorState?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT f.secret, f.created_at, f.enabled_at, f.last_used_step,
                   (SELECT count(*) FROM access.recovery_code c WHERE c.user_id = f.user_id AND c.used_at IS NULL)
            FROM access.user_second_factor f
            WHERE f.user_id = $1
            """);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SecondFactorState(
            ProtectedSecret: reader.GetString(0),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(1),
            EnabledAt: reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            LastUsedStep: reader.IsDBNull(3) ? null : reader.GetInt64(3),
            RecoveryCodesLeft: (int)reader.GetInt64(4));
    }

    /// <summary>The people whose second factor is switched on, and since when.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Per person: when it was switched on.</returns>
    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ListEnabledAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT user_id, enabled_at FROM access.user_second_factor WHERE enabled_at IS NOT NULL");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var enabled = new Dictionary<Guid, DateTimeOffset>();

        while (await reader.ReadAsync(cancellationToken))
        {
            enabled[reader.GetGuid(0)] = reader.GetFieldValue<DateTimeOffset>(1);
        }

        return enabled;
    }

    /// <summary>
    /// Starts setup with a new secret, replacing an earlier setup that was not finished. Does nothing when the second
    /// factor is already switched on.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="protectedSecret">The encrypted secret.</param>
    /// <param name="now">The time setup starts.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns><see langword="true"/> when setup started; <see langword="false"/> when it is already on.</returns>
    public async Task<bool> StartSetupAsync(
        Guid userId,
        string protectedSecret,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO access.user_second_factor (user_id, secret, created_at)
            VALUES ($1, $2, $3)
            ON CONFLICT (user_id) DO UPDATE
                SET secret = excluded.secret, created_at = excluded.created_at, last_used_step = NULL
                WHERE access.user_second_factor.enabled_at IS NULL
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(protectedSecret);
        command.Parameters.AddWithValue(now);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>
    /// Switches the second factor on with its first accepted code, and stores a new set of recovery codes, in one
    /// transaction.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="step">The step of the code that confirmed setup.</param>
    /// <param name="codeHashes">The hashes of the new recovery codes.</param>
    /// <param name="now">The time it is switched on.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>
    /// <see langword="true"/> when it was switched on; <see langword="false"/> when no setup was waiting.
    /// </returns>
    public async Task<bool> EnableAsync(
        Guid userId,
        long step,
        IReadOnlyList<byte[]> codeHashes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var enable = new NpgsqlCommand(
            """
            UPDATE access.user_second_factor SET enabled_at = $2, last_used_step = $3
            WHERE user_id = $1 AND enabled_at IS NULL
            """,
            connection))
        {
            enable.Parameters.AddWithValue(userId);
            enable.Parameters.AddWithValue(now);
            enable.Parameters.AddWithValue(step);

            if (await enable.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return false;
            }
        }

        await ReplaceCodesAsync(connection, userId, codeHashes, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Records the step of an accepted code, unless an equal or later step was accepted already, so two requests
    /// with the same code cannot both pass.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="step">The step of the accepted code.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><see langword="true"/> when this request may use the code.</returns>
    public async Task<bool> UseStepAsync(Guid userId, long step, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE access.user_second_factor SET last_used_step = $2
            WHERE user_id = $1 AND enabled_at IS NOT NULL AND (last_used_step IS NULL OR last_used_step < $2)
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(step);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>Uses up a recovery code.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="codeHash">The hash of the typed code.</param>
    /// <param name="now">The time it is used.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><see langword="true"/> when the code existed and was not used before.</returns>
    public async Task<bool> UseRecoveryCodeAsync(
        Guid userId,
        byte[] codeHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE access.recovery_code SET used_at = $3
            WHERE user_id = $1 AND code_hash = $2 AND used_at IS NULL
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(codeHash);
        command.Parameters.AddWithValue(now);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>Replaces all recovery codes with a new set.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="codeHashes">The hashes of the new codes.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the codes are replaced.</returns>
    public async Task ReplaceRecoveryCodesAsync(
        Guid userId,
        IReadOnlyList<byte[]> codeHashes,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ReplaceCodesAsync(connection, userId, codeHashes, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Removes a person's second factor and recovery codes.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns><see langword="true"/> when a switched-on second factor was removed.</returns>
    public async Task<bool> RemoveAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            WITH codes AS (DELETE FROM access.recovery_code WHERE user_id = $1)
            DELETE FROM access.user_second_factor WHERE user_id = $1 RETURNING enabled_at IS NOT NULL
            """);
        command.Parameters.AddWithValue(userId);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task ReplaceCodesAsync(
        NpgsqlConnection connection,
        Guid userId,
        IReadOnlyList<byte[]> codeHashes,
        CancellationToken cancellationToken)
    {
        await using (var delete = new NpgsqlCommand("DELETE FROM access.recovery_code WHERE user_id = $1", connection))
        {
            delete.Parameters.AddWithValue(userId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = new NpgsqlCommand(
            "INSERT INTO access.recovery_code (user_id, code_hash) SELECT $1, unnest($2::bytea[])",
            connection);
        insert.Parameters.AddWithValue(userId);
        insert.Parameters.AddWithValue(codeHashes.ToArray());

        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
