using Npgsql;
using Veyra.Core.Security;

namespace Veyra.Access.Users;

/// <summary>
/// Reads and writes users in <c>access.user_account</c>.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class UserStore(NpgsqlDataSource dataSource)
{
    private const string _columns =
        "id, email, display_name, role, disabled, password_hash, failed_sign_ins, locked_until, created_at, "
        + "last_sign_in_at";

    // Serialises first-run setup, so two people using the setup link at the same moment cannot both become admin.
    private const long _setupLockKey = 0x5665797261_01;

    // PostgreSQL's error code for a violated unique constraint.
    private const string _uniqueViolation = "23505";

    /// <summary>Counts all users, including disabled ones.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of users.</returns>
    public async Task<long> CountAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM access.user_account");

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Counts administrators who are not disabled.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of active administrators.</returns>
    public async Task<long> CountActiveAdminsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT count(*) FROM access.user_account WHERE role = $1 AND NOT disabled");
        command.Parameters.AddWithValue((short)VeyraRole.Admin);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Finds a user by email, ignoring case.</summary>
    /// <param name="email">The email address.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user, or <see langword="null"/>.</returns>
    public async Task<AccessUser?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM access.user_account WHERE email_normalized = $1");
        command.Parameters.AddWithValue(AccessUser.Normalize(email));

        return await ReadSingleAsync(command, cancellationToken);
    }

    /// <summary>Finds a user by ID.</summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user, or <see langword="null"/>.</returns>
    public async Task<AccessUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM access.user_account WHERE id = $1");
        command.Parameters.AddWithValue(id);

        return await ReadSingleAsync(command, cancellationToken);
    }

    /// <summary>Lists all users, ordered by email.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The users.</returns>
    public async Task<IReadOnlyList<AccessUser>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM access.user_account ORDER BY email_normalized");

        return await ReadAllAsync(command, cancellationToken);
    }

    /// <summary>Stores a new user.</summary>
    /// <param name="user">The user to store.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the user is stored.</returns>
    /// <exception cref="AccessConflictException">A user with the same email already exists.</exception>
    public async Task InsertAsync(AccessUser user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await InsertAsync(connection, user, cancellationToken);
    }

    /// <summary>
    /// Stores the first administrator, but only while no user exists.
    /// </summary>
    /// <param name="user">The administrator to store.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>
    /// <see langword="true"/> when the user was stored; <see langword="false"/> when users already exist.
    /// </returns>
    public async Task<bool> InsertFirstAdminAsync(AccessUser user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({_setupLockKey})", connection))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var countCommand = new NpgsqlCommand("SELECT count(*) FROM access.user_account", connection))
        {
            if ((long)(await countCommand.ExecuteScalarAsync(cancellationToken))! > 0)
            {
                return false;
            }
        }

        await InsertAsync(connection, user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>Changes a user's role, disabled flag, or both.</summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="role">The new role, or <see langword="null"/> to keep it.</param>
    /// <param name="disabled">The new disabled flag, or <see langword="null"/> to keep it.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><see langword="true"/> when the user exists.</returns>
    public async Task<bool> UpdateAsync(Guid id, VeyraRole? role, bool? disabled, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE access.user_account
            SET role = coalesce($2, role),
                disabled = coalesce($3, disabled)
            WHERE id = $1
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Smallint, role is null ? DBNull.Value : (short)role);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Boolean, disabled is null ? DBNull.Value : disabled);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>Records a failed sign-in, and a lockout when the threshold is reached.</summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="failedSignIns">The new number of failures in a row.</param>
    /// <param name="lockedUntil">When the lockout ends, or <see langword="null"/> for no lockout.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the failure is recorded.</returns>
    public async Task RecordFailedSignInAsync(
        Guid id,
        int failedSignIns,
        DateTimeOffset? lockedUntil,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE access.user_account SET failed_sign_ins = $2, locked_until = $3 WHERE id = $1");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(failedSignIns);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)lockedUntil ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Records a successful sign-in and clears failures and lockout.</summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="signedInAt">The time of the sign-in.</param>
    /// <param name="newPasswordHash">
    /// A stronger hash of the same password, or <see langword="null"/> to keep it.
    /// </param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the sign-in is recorded.</returns>
    public async Task RecordSignInAsync(
        Guid id,
        DateTimeOffset signedInAt,
        string? newPasswordHash,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE access.user_account
            SET failed_sign_ins = 0,
                locked_until = NULL,
                last_sign_in_at = $2,
                password_hash = coalesce($3, password_hash)
            WHERE id = $1
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(signedInAt);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)newPasswordHash ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Replaces a user's password hash.</summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="passwordHash">The new hash.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the hash is stored.</returns>
    public async Task SetPasswordHashAsync(Guid id, string passwordHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE access.user_account SET password_hash = $2 WHERE id = $1");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(passwordHash);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        AccessUser user,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO access.user_account
                (id, email, email_normalized, display_name, password_hash, role, disabled, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """,
            connection);

        command.Parameters.AddWithValue(user.Id);
        command.Parameters.AddWithValue(user.Email.Trim());
        command.Parameters.AddWithValue(AccessUser.Normalize(user.Email));
        command.Parameters.AddWithValue(user.DisplayName);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)user.PasswordHash ?? DBNull.Value);
        command.Parameters.AddWithValue((short)user.Role);
        command.Parameters.AddWithValue(user.Disabled);
        command.Parameters.AddWithValue(user.CreatedAt);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == _uniqueViolation)
        {
            throw new AccessConflictException("A user with this email address already exists.");
        }
    }

    private static async Task<AccessUser?> ReadSingleAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var users = await ReadAllAsync(command, cancellationToken);

        return users.Count == 0 ? null : users[0];
    }

    private static async Task<IReadOnlyList<AccessUser>> ReadAllAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var users = new List<AccessUser>();

        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new AccessUser(
                Id: reader.GetGuid(0),
                Email: reader.GetString(1),
                DisplayName: reader.GetString(2),
                Role: (VeyraRole)reader.GetInt16(3),
                Disabled: reader.GetBoolean(4),
                PasswordHash: reader.IsDBNull(5) ? null : reader.GetString(5),
                FailedSignIns: reader.GetInt32(6),
                LockedUntil: reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(8),
                LastSignInAt: reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return users;
    }
}
