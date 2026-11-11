using Npgsql;

namespace Veyra.Access.History;

/// <summary>Records and reads changes to accounts in <c>access.user_event</c> (ADR 0011).</summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class UserEventStore(NpgsqlDataSource dataSource)
{
    /// <summary>Records a change.</summary>
    /// <param name="userId">The account that changed.</param>
    /// <param name="at">When.</param>
    /// <param name="kind">What changed.</param>
    /// <param name="actorId">Who changed it, or <see langword="null"/> when Veyra did.</param>
    /// <param name="detail">What it changed to, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the change is recorded.</returns>
    public async Task RecordAsync(
        Guid userId,
        DateTimeOffset at,
        UserEventKind kind,
        Guid? actorId,
        string? detail,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO access.user_event (user_id, at, kind, actor_id, detail) VALUES ($1, $2, $3, $4, $5)");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(at);
        command.Parameters.AddWithValue((short)kind);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)actorId ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)detail ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Lists an account's changes, newest first, with the names of who made them.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The changes.</returns>
    public async Task<IReadOnlyList<UserEvent>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT e.at, e.kind, e.actor_id, a.display_name, e.detail
            FROM access.user_event e
            LEFT JOIN access.user_account a ON a.id = e.actor_id
            WHERE e.user_id = $1
            ORDER BY e.at DESC, e.id DESC
            """);
        command.Parameters.AddWithValue(userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<UserEvent>();

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new UserEvent(
                At: reader.GetFieldValue<DateTimeOffset>(0),
                Kind: (UserEventKind)reader.GetInt16(1),
                ActorId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                ActorName: reader.IsDBNull(3) ? null : reader.GetString(3),
                Detail: reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return events;
    }
}
