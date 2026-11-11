using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;
using Veyra.Access.Users;

namespace Veyra.Access.Sessions;

/// <summary>
/// Reads and writes sessions in <c>access.session</c> (ADR 0011). A session is active while it is not ended and its
/// last activity is within the idle timeout.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class SessionStore(NpgsqlDataSource dataSource)
{
    /// <summary>How stale last activity may get before a request writes it again.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    private const string _columns =
        "s.id, s.user_id, s.started_at, s.last_active_at, s.ended_at, s.address, s.user_agent, s.method";

    private const int _columnCount = 8;

    /// <summary>Makes a new random session ID: 32 bytes, Base64url.</summary>
    /// <returns>The ID.</returns>
    public static string NewId() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>Stores a new session.</summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the session is stored.</returns>
    public async Task InsertAsync(AccessSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO access.session (id, user_id, started_at, last_active_at, address, user_agent, method)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            """);
        command.Parameters.AddWithValue(session.Id);
        command.Parameters.AddWithValue(session.UserId);
        command.Parameters.AddWithValue(session.StartedAt);
        command.Parameters.AddWithValue(session.LastActiveAt);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)session.Address ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)session.UserAgent ?? DBNull.Value);
        command.Parameters.AddWithValue((short)session.Method);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Finds a session together with its user, for the check every request makes.
    /// </summary>
    /// <param name="id">The session ID from the cookie.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The session and its user, or <see langword="null"/> when the ID is unknown.</returns>
    public async Task<(AccessSession Session, AccessUser User)?> FindWithUserAsync(
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {_columns}, {UserStore.Columns}
            FROM access.session s
            JOIN access.user_account u ON u.id = s.user_id
            WHERE s.id = $1
            """);
        command.Parameters.AddWithValue(id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? (Read(reader), UserStore.ReadUser(reader, _columnCount))
            : null;
    }

    /// <summary>Records activity, at most once per <see cref="TouchInterval"/>.</summary>
    /// <param name="id">The session ID.</param>
    /// <param name="now">The time of the request.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the activity is recorded or was recent enough.</returns>
    public async Task TouchAsync(string id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE access.session SET last_active_at = $2 WHERE id = $1 AND ended_at IS NULL AND last_active_at < $3");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(now - TouchInterval);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Lists a person's active sessions, newest first.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="activeSince">Sessions idle since before this have expired.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The sessions.</returns>
    public async Task<IReadOnlyList<AccessSession>> ListActiveAsync(
        Guid userId,
        DateTimeOffset activeSince,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {_columns}
            FROM access.session s
            WHERE s.user_id = $1 AND s.ended_at IS NULL AND s.last_active_at >= $2
            ORDER BY s.last_active_at DESC
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(activeSince);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sessions = new List<AccessSession>();

        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(Read(reader));
        }

        return sessions;
    }

    /// <summary>
    /// For every person, the number of active sessions and the newest activity of any session, for the Users list.
    /// </summary>
    /// <param name="activeSince">Sessions idle since before this have expired.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Per person: active sessions and last activity.</returns>
    public async Task<IReadOnlyDictionary<Guid, (int Active, DateTimeOffset LastActive)>> SummarizeAsync(
        DateTimeOffset activeSince,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT user_id,
                   count(*) FILTER (WHERE ended_at IS NULL AND last_active_at >= $1),
                   max(last_active_at)
            FROM access.session
            GROUP BY user_id
            """);
        command.Parameters.AddWithValue(activeSince);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var summary = new Dictionary<Guid, (int, DateTimeOffset)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            summary[reader.GetGuid(0)] = ((int)reader.GetInt64(1), reader.GetFieldValue<DateTimeOffset>(2));
        }

        return summary;
    }

    /// <summary>Ends one of a person's sessions.</summary>
    /// <param name="userId">The person, so nobody ends someone else's session by ID.</param>
    /// <param name="id">The session ID.</param>
    /// <param name="now">The time it ends.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><see langword="true"/> when an open session was ended.</returns>
    public async Task<bool> EndAsync(Guid userId, string id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE access.session SET ended_at = $3 WHERE user_id = $1 AND id = $2 AND ended_at IS NULL");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(now);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>Ends all of a person's open sessions, except one if given.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="exceptId">
    /// The session to keep, usually the one making the request, or <see langword="null"/>.
    /// </param>
    /// <param name="now">The time they end.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>How many sessions were ended.</returns>
    public async Task<int> EndAllAsync(
        Guid userId,
        string? exceptId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE access.session SET ended_at = $3
            WHERE user_id = $1 AND ended_at IS NULL AND ($2::text IS NULL OR id <> $2::text)
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)exceptId ?? DBNull.Value);
        command.Parameters.AddWithValue(now);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Deletes sessions that ended, or went idle, before a time.</summary>
    /// <param name="before">The oldest time kept.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How many sessions were deleted.</returns>
    public async Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM access.session WHERE coalesce(ended_at, last_active_at) < $1");
        command.Parameters.AddWithValue(before);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AccessSession Read(NpgsqlDataReader reader) => new(
        Id: reader.GetString(0),
        UserId: reader.GetGuid(1),
        StartedAt: reader.GetFieldValue<DateTimeOffset>(2),
        LastActiveAt: reader.GetFieldValue<DateTimeOffset>(3),
        EndedAt: reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
        Address: reader.IsDBNull(5) ? null : reader.GetString(5),
        UserAgent: reader.IsDBNull(6) ? null : reader.GetString(6),
        Method: (SessionMethod)reader.GetInt16(7));
}
