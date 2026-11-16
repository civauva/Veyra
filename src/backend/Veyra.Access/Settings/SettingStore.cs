using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Veyra.Core;

namespace Veyra.Access.Settings;

/// <summary>
/// The settings saved on the Settings screen, in <c>access.setting</c>, and the history of every change, in
/// <c>access.setting_change</c> (ADR 0017).
/// </summary>
/// <remarks>
/// A save names the version it started from, the number of the last change it saw. It is refused when another save
/// came first, so two Admins cannot overwrite each other without seeing it.
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="protection">Encrypts secret values, with keys kept apart from the database.</param>
/// <param name="clock">Stamps each change.</param>
internal sealed class SettingStore(
    NpgsqlDataSource dataSource,
    IDataProtectionProvider protection,
    TimeProvider clock)
{
    /// <summary>The purpose secrets are encrypted under; the configuration source decrypts with the same.</summary>
    public const string SecretPurpose = "Veyra.Access.Setting.v1";

    private readonly IDataProtector _protector = protection.CreateProtector(SecretPurpose);

    /// <summary>The connection pool, for tests that look at the rows themselves.</summary>
    internal NpgsqlDataSource DataSource => dataSource;

    /// <summary>The number of the last change, which a save must name.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The version; 0 before the first change.</returns>
    public async Task<long> VersionAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT coalesce(max(id), 0) FROM access.setting_change");

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Reads every saved setting, with secrets decrypted.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The saved settings by key. Secrets that can no longer be decrypted are left out.</returns>
    public async Task<IReadOnlyDictionary<string, SavedSetting>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT s.name, s.value, s.secret, c.changed_by_name, s.changed_at
            FROM access.setting s
            LEFT JOIN LATERAL (
                SELECT changed_by_name FROM access.setting_change
                WHERE name = s.name ORDER BY id DESC LIMIT 1
            ) c ON true
            WHERE s.name LIKE $1
            """);
        command.Parameters.AddWithValue(VeyraOptionNames.Root + ":%");

        var settings = new Dictionary<string, SavedSetting>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var secret = reader.GetBoolean(2);
            var value = secret ? Unprotect(reader.GetString(1)) : reader.GetString(1);

            if (value is not null)
            {
                settings[reader.GetString(0)] = new SavedSetting(
                    value,
                    secret,
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4));
            }
        }

        return settings;
    }

    /// <summary>
    /// Saves a batch of settings in one transaction, all or nothing, and records each change in the history.
    /// </summary>
    /// <param name="expectedVersion">The version the change was made from.</param>
    /// <param name="writes">The settings to write; a null value removes the saved value.</param>
    /// <param name="author">Who saves it.</param>
    /// <param name="kind">How the change came about.</param>
    /// <param name="origin">The file name of an import; null otherwise.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The new version; null when another save came first and nothing was written.</returns>
    public async Task<long?> SaveAsync(
        long expectedVersion,
        IReadOnlyList<SettingWrite> writes,
        SettingAuthor author,
        SettingChangeKind kind,
        string? origin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(author);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // One save at a time, so the version check and the writes cannot interleave with another save.
        await ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtext('access.setting'))", cancellationToken);

        await using (
            var current = new NpgsqlCommand("SELECT coalesce(max(id), 0) FROM access.setting_change", connection))
        {
            if ((long)(await current.ExecuteScalarAsync(cancellationToken))! != expectedVersion)
            {
                return null;
            }
        }

        var now = clock.GetUtcNow();
        var version = expectedVersion;

        foreach (var write in writes)
        {
            var old = await ReadOneAsync(connection, write.Name, cancellationToken);

            if (!write.Secret && old == write.Value)
            {
                continue;
            }

            await WriteValueAsync(connection, write, author, now, cancellationToken);
            version = await RecordAsync(connection, write, old, author, kind, origin, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return version;
    }

    /// <summary>Reads a page of the history, newest first.</summary>
    /// <param name="query">Which changes to read.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page.</returns>
    public async Task<SettingHistoryPage> HistoryAsync(SettingHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var command = dataSource.CreateCommand(
            """
            SELECT id, name, old_value, new_value, secret, kind, changed_by, changed_by_name, changed_at, origin
            FROM access.setting_change
            WHERE ($1::text IS NULL OR name LIKE $1 || '%')
              AND ($2::uuid IS NULL OR changed_by = $2)
              AND ($3::timestamptz IS NULL OR changed_at >= $3)
              AND ($4::bigint IS NULL OR id < $4)
              AND ($6::text[] IS NULL OR name = ANY($6))
            ORDER BY id DESC
            LIMIT $5
            """);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)query.Prefix ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)query.ChangedBy ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)query.From ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Bigint, (object?)query.Before ?? DBNull.Value);
        command.Parameters.AddWithValue(query.Limit + 1);
        command.Parameters.AddWithValue(
            NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text,
            (object?)query.Names?.ToArray() ?? DBNull.Value);

        var changes = new List<SettingChangeRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            changes.Add(Change(reader));
        }

        var more = changes.Count > query.Limit;

        return more
            ? new SettingHistoryPage(changes[..query.Limit], changes[query.Limit - 1].Id)
            : new SettingHistoryPage(changes, null);
    }

    /// <summary>Reads one change of the history.</summary>
    /// <param name="id">The change's number.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The change; null when there is none with that number.</returns>
    public async Task<SettingChangeRecord?> GetChangeAsync(long id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, name, old_value, new_value, secret, kind, changed_by, changed_by_name, changed_at, origin
            FROM access.setting_change WHERE id = $1
            """);
        command.Parameters.AddWithValue(id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Change(reader) : null;
    }

    /// <summary>Deletes history older than the given time. Saved values are kept.</summary>
    /// <param name="cutoff">Changes saved before this are deleted.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>The number of changes deleted.</returns>
    public async Task<int> DeleteHistoryBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM access.setting_change WHERE changed_at < $1");
        command.Parameters.AddWithValue(cutoff);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string? Unprotect(string value)
    {
        try
        {
            return _protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            // The key ring was lost or replaced; the secret has to be entered again.
            return null;
        }
    }

    private static async Task<string?> ReadOneAsync(
        NpgsqlConnection connection,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT value FROM access.setting WHERE name = $1 FOR UPDATE",
            connection);
        command.Parameters.AddWithValue(name);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async Task WriteValueAsync(
        NpgsqlConnection connection,
        SettingWrite write,
        SettingAuthor author,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (write.Value is null)
        {
            await using var delete = new NpgsqlCommand("DELETE FROM access.setting WHERE name = $1", connection);
            delete.Parameters.AddWithValue(write.Name);
            await delete.ExecuteNonQueryAsync(cancellationToken);

            return;
        }

        await using var upsert = new NpgsqlCommand(
            """
            INSERT INTO access.setting (name, value, secret, changed_by, changed_at) VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (name) DO UPDATE SET value = excluded.value, secret = excluded.secret,
                changed_by = excluded.changed_by, changed_at = excluded.changed_at
            """,
            connection);
        upsert.Parameters.AddWithValue(write.Name);
        upsert.Parameters.AddWithValue(write.Secret ? _protector.Protect(write.Value) : write.Value);
        upsert.Parameters.AddWithValue(write.Secret);
        upsert.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)author.UserId ?? DBNull.Value);
        upsert.Parameters.AddWithValue(now);
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> RecordAsync(
        NpgsqlConnection connection,
        SettingWrite write,
        string? old,
        SettingAuthor author,
        SettingChangeKind kind,
        string? origin,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO access.setting_change
                (name, old_value, new_value, secret, kind, changed_by, changed_by_name, changed_at, origin)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            RETURNING id
            """,
            connection);
        command.Parameters.AddWithValue(write.Name);
        command.Parameters.AddWithValue(
            NpgsqlTypes.NpgsqlDbType.Text,
            write.Secret ? DBNull.Value : (object?)old ?? DBNull.Value);
        command.Parameters.AddWithValue(
            NpgsqlTypes.NpgsqlDbType.Text,
            write.Secret ? DBNull.Value : (object?)write.Value ?? DBNull.Value);
        command.Parameters.AddWithValue(write.Secret);
        command.Parameters.AddWithValue((short)kind);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)author.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue(author.Name);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)origin ?? DBNull.Value);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SettingChangeRecord Change(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetBoolean(4),
        (SettingChangeKind)reader.GetInt16(5),
        reader.IsDBNull(6) ? null : reader.GetGuid(6),
        reader.GetString(7),
        reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetString(9));
}
