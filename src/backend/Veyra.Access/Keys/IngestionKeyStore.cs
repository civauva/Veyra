using Npgsql;

namespace Veyra.Access.Keys;

/// <summary>
/// Reads and writes ingestion keys in <c>access.ingestion_key</c>.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class IngestionKeyStore(NpgsqlDataSource dataSource)
{
    private const string _columns =
        "id, name, display_prefix, environments, created_by, created_at, expires_at, revoked_at, last_used_at";

    /// <summary>Stores a new key with the hash of its secret.</summary>
    /// <param name="key">The key to store.</param>
    /// <param name="hash">The SHA-256 hash of the secret.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the key is stored.</returns>
    public async Task InsertAsync(IngestionKey key, byte[] hash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO access.ingestion_key
                (id, name, display_prefix, key_hash, environments, created_by, created_at, expires_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """);

        command.Parameters.AddWithValue(key.Id);
        command.Parameters.AddWithValue(key.Name);
        command.Parameters.AddWithValue(key.DisplayPrefix);
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(
            NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text,
            (object?)key.Environments?.ToArray() ?? DBNull.Value);
        command.Parameters.AddWithValue(key.CreatedBy);
        command.Parameters.AddWithValue(key.CreatedAt);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)key.ExpiresAt ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Lists all keys, newest first, including revoked ones.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The keys.</returns>
    public async Task<IReadOnlyList<IngestionKey>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM access.ingestion_key ORDER BY created_at DESC");

        return await ReadAllAsync(command, cancellationToken);
    }

    /// <summary>Finds the key whose secret has the given hash.</summary>
    /// <param name="hash">The SHA-256 hash of a presented secret.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The key, or <see langword="null"/> when no key has that hash.</returns>
    public async Task<IngestionKey?> FindByHashAsync(byte[] hash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM access.ingestion_key WHERE key_hash = $1");
        command.Parameters.AddWithValue(hash);

        var keys = await ReadAllAsync(command, cancellationToken);

        return keys.Count == 0 ? null : keys[0];
    }

    /// <summary>Marks an active key as revoked.</summary>
    /// <param name="id">The key's ID.</param>
    /// <param name="revokedAt">The time of revocation.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><see langword="true"/> when an active key was revoked; <see langword="false"/> otherwise.</returns>
    public async Task<bool> RevokeAsync(Guid id, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE access.ingestion_key SET revoked_at = $2 WHERE id = $1 AND revoked_at IS NULL");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(revokedAt);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>Records when a key was last presented.</summary>
    /// <param name="id">The key's ID.</param>
    /// <param name="usedAt">The time of use.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the time is recorded.</returns>
    public async Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE access.ingestion_key SET last_used_at = $2 WHERE id = $1");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(usedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<IngestionKey>> ReadAllAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var keys = new List<IngestionKey>();

        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(new IngestionKey(
                Id: reader.GetGuid(0),
                Name: reader.GetString(1),
                DisplayPrefix: reader.GetString(2),
                Environments: reader.IsDBNull(3) ? null : reader.GetFieldValue<string[]>(3),
                CreatedBy: reader.GetGuid(4),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(5),
                ExpiresAt: reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                RevokedAt: reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                LastUsedAt: reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return keys;
    }
}
