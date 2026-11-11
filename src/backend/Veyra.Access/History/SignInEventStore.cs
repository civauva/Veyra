using Npgsql;

namespace Veyra.Access.History;

/// <summary>Records and reads sign-in attempts in <c>access.sign_in_event</c> (ADR 0011).</summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class SignInEventStore(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Records an attempt. It is marked as from a new address when the account has signed in before, but never from
    /// this address within the kept history.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="at">When it happened.</param>
    /// <param name="outcome">How it ended.</param>
    /// <param name="address">The client address, or <see langword="null"/>.</param>
    /// <param name="userAgent">The browser, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>Whether the address was new.</returns>
    public async Task<bool> RecordAsync(
        Guid userId,
        DateTimeOffset at,
        SignInOutcome outcome,
        string? address,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        // Successful sign-ins are 1 to 3. The first sign-in of an account is not "new": there is nothing to compare.
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO access.sign_in_event (user_id, at, outcome, address, user_agent, new_address)
            SELECT $1, $2, $3, $4, $5,
                   $4::text IS NOT NULL
                   AND EXISTS (SELECT 1 FROM access.sign_in_event WHERE user_id = $1 AND outcome <= 3)
                   AND NOT EXISTS (
                       SELECT 1 FROM access.sign_in_event WHERE user_id = $1 AND outcome <= 3 AND address = $4::text)
            RETURNING new_address
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(at);
        command.Parameters.AddWithValue((short)outcome);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)address ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)userAgent ?? DBNull.Value);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Lists an account's latest attempts, newest first.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="limit">The most attempts to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The attempts.</returns>
    public async Task<IReadOnlyList<SignInEvent>> ListAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT at, outcome, address, user_agent, new_address
            FROM access.sign_in_event
            WHERE user_id = $1
            ORDER BY at DESC, id DESC
            LIMIT $2
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<SignInEvent>();

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new SignInEvent(
                At: reader.GetFieldValue<DateTimeOffset>(0),
                Outcome: (SignInOutcome)reader.GetInt16(1),
                Address: reader.IsDBNull(2) ? null : reader.GetString(2),
                UserAgent: reader.IsDBNull(3) ? null : reader.GetString(3),
                NewAddress: reader.GetBoolean(4)));
        }

        return events;
    }

    /// <summary>Counts an account's successful and failed attempts since a time.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="since">The start of the count.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Attempts that signed in, and attempts that did not.</returns>
    public async Task<(int SignedIn, int Failed)> CountAsync(
        Guid userId,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT count(*) FILTER (WHERE outcome <= 3), count(*) FILTER (WHERE outcome > 3)
            FROM access.sign_in_event
            WHERE user_id = $1 AND at >= $2
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(since);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1));
    }

    /// <summary>Deletes attempts older than a time.</summary>
    /// <param name="before">The oldest time kept.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How many attempts were deleted.</returns>
    public async Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM access.sign_in_event WHERE at < $1");
        command.Parameters.AddWithValue(before);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
