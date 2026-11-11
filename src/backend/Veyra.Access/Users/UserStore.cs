using Npgsql;
using Veyra.Core.Security;

namespace Veyra.Access.Users;

/// <summary>
/// Reads and writes users in <c>access.user_account</c>.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class UserStore(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// The columns <see cref="ReadUser"/> reads, in order, so other stores can join users without a second query.
    /// The table must be aliased <c>u</c>.
    /// </summary>
    internal const string Columns =
        "u.id, u.email, u.display_name, u.role, u.disabled, u.password_hash, u.failed_sign_ins, u.locked_until, "
        + "u.created_at, u.last_sign_in_at, u.team";

    /// <summary>How many columns <see cref="Columns"/> has.</summary>
    internal const int ColumnCount = 11;

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
            $"SELECT {Columns} FROM access.user_account u WHERE u.email_normalized = $1");
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
            $"SELECT {Columns} FROM access.user_account u WHERE u.id = $1");
        command.Parameters.AddWithValue(id);

        return await ReadSingleAsync(command, cancellationToken);
    }

    /// <summary>Lists all users, ordered by email.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The users.</returns>
    public async Task<IReadOnlyList<AccessUser>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {Columns} FROM access.user_account u ORDER BY u.email_normalized");

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

    /// <summary>Changes a user's role, disabled flag, team, or several.</summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="role">The new role, or <see langword="null"/> to keep it.</param>
    /// <param name="disabled">The new disabled flag, or <see langword="null"/> to keep it.</param>
    /// <param name="team">The new team, an empty string to remove it, or <see langword="null"/> to keep it.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><see langword="true"/> when the user exists.</returns>
    public async Task<bool> UpdateAsync(
        Guid id,
        VeyraRole? role,
        bool? disabled,
        string? team,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE access.user_account
            SET role = coalesce($2, role),
                disabled = coalesce($3, disabled),
                team = CASE WHEN $4::text IS NULL THEN team ELSE nullif($4::text, '') END
            WHERE id = $1
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Smallint, role is null ? DBNull.Value : (short)role);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Boolean, disabled is null ? DBNull.Value : disabled);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)team ?? DBNull.Value);

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

    /// <summary>
    /// Replaces a user's password hash and lifts a lockout, since the wrong attempts were against the old password.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="passwordHash">The new hash.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the hash is stored.</returns>
    public async Task SetPasswordHashAsync(Guid id, string passwordHash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE access.user_account
            SET password_hash = $2,
                failed_sign_ins = 0,
                locked_until = NULL
            WHERE id = $1
            """);
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
                (id, email, email_normalized, display_name, password_hash, role, disabled, created_at, team)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
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
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)user.Team ?? DBNull.Value);

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
            users.Add(ReadUser(reader, 0));
        }

        return users;
    }

    /// <summary>Reads a user from the <see cref="Columns"/> of a row, starting at a column.</summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="first">The column of <c>u.id</c>.</param>
    /// <returns>The user.</returns>
    internal static AccessUser ReadUser(NpgsqlDataReader reader, int first) => new(
        Id: reader.GetGuid(first),
        Email: reader.GetString(first + 1),
        DisplayName: reader.GetString(first + 2),
        Role: (VeyraRole)reader.GetInt16(first + 3),
        Disabled: reader.GetBoolean(first + 4),
        PasswordHash: reader.IsDBNull(first + 5) ? null : reader.GetString(first + 5),
        FailedSignIns: reader.GetInt32(first + 6),
        LockedUntil: reader.IsDBNull(first + 7) ? null : reader.GetFieldValue<DateTimeOffset>(first + 7),
        CreatedAt: reader.GetFieldValue<DateTimeOffset>(first + 8),
        LastSignInAt: reader.IsDBNull(first + 9) ? null : reader.GetFieldValue<DateTimeOffset>(first + 9),
        Team: reader.IsDBNull(first + 10) ? null : reader.GetString(first + 10));
}
