using Npgsql;

namespace Veyra.Access.Views;

/// <summary>Reads and writes saved views in <c>access.saved_view</c> (ADR 0024).</summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class SavedViewStore(NpgsqlDataSource dataSource)
{
    private const string _columns =
        "v.id, v.owner_id, u.display_name, v.screen, v.name, v.query, v.shared, v.created_at, v.updated_at";

    /// <summary>
    /// Lists the views of a screen a person sees: their own first, then those others shared, each by name.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="screen">The screen.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The views.</returns>
    public async Task<IReadOnlyList<SavedView>> ListAsync(
        Guid userId,
        string screen,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {_columns}
            FROM access.saved_view v
            JOIN access.user_account u ON u.id = v.owner_id
            WHERE v.screen = $2 AND (v.owner_id = $1 OR v.shared)
            ORDER BY v.owner_id <> $1, lower(v.name), v.created_at, v.id
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(screen);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var views = new List<SavedView>();

        while (await reader.ReadAsync(cancellationToken))
        {
            views.Add(Read(reader));
        }

        return views;
    }

    /// <summary>Finds a view by ID, whoever owns it.</summary>
    /// <param name="id">The view's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The view, or <see langword="null"/>.</returns>
    public async Task<SavedView?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {_columns}
            FROM access.saved_view v
            JOIN access.user_account u ON u.id = v.owner_id
            WHERE v.id = $1
            """);
        command.Parameters.AddWithValue(id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    /// <summary>
    /// Stores a new view, unless its owner already keeps <paramref name="limit"/> views. The owner's row is locked
    /// while counting, so two saves at the same moment cannot both pass the limit.
    /// </summary>
    /// <param name="view">The view; its owner name is not stored.</param>
    /// <param name="limit">The most views one person keeps, of all screens together.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns><see langword="false"/> when the owner is at the limit, or no longer exists.</returns>
    public async Task<bool> InsertAsync(SavedView view, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var owner = new NpgsqlCommand(
            "SELECT 1 FROM access.user_account WHERE id = $1 FOR UPDATE",
            connection,
            transaction))
        {
            owner.Parameters.AddWithValue(view.OwnerId);

            if (await owner.ExecuteScalarAsync(cancellationToken) is null)
            {
                return false;
            }
        }

        await using (var count = new NpgsqlCommand(
            "SELECT count(*) FROM access.saved_view WHERE owner_id = $1",
            connection,
            transaction))
        {
            count.Parameters.AddWithValue(view.OwnerId);

            if ((long)(await count.ExecuteScalarAsync(cancellationToken))! >= limit)
            {
                return false;
            }
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO access.saved_view (id, owner_id, screen, name, query, shared, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(view.Id);
            insert.Parameters.AddWithValue(view.OwnerId);
            insert.Parameters.AddWithValue(view.Screen);
            insert.Parameters.AddWithValue(view.Name);
            insert.Parameters.AddWithValue(view.Query);
            insert.Parameters.AddWithValue(view.Shared);
            insert.Parameters.AddWithValue(view.CreatedAt);
            insert.Parameters.AddWithValue(view.UpdatedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>Changes a view's name, query, and sharing, if its owner is still the one given.</summary>
    /// <param name="view">The view with its new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><see langword="false"/> when the view is gone.</returns>
    public async Task<bool> UpdateAsync(SavedView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);

        await using var command = dataSource.CreateCommand(
            """
            UPDATE access.saved_view
            SET name = $3, query = $4, shared = $5, updated_at = $6
            WHERE id = $1 AND owner_id = $2
            """);
        command.Parameters.AddWithValue(view.Id);
        command.Parameters.AddWithValue(view.OwnerId);
        command.Parameters.AddWithValue(view.Name);
        command.Parameters.AddWithValue(view.Query);
        command.Parameters.AddWithValue(view.Shared);
        command.Parameters.AddWithValue(view.UpdatedAt);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Deletes a view, if the given person owns it.</summary>
    /// <param name="id">The view's ID.</param>
    /// <param name="ownerId">Its owner.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns><see langword="false"/> when the view is gone.</returns>
    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM access.saved_view WHERE id = $1 AND owner_id = $2");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(ownerId);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static SavedView Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetBoolean(6),
        reader.GetFieldValue<DateTimeOffset>(7),
        reader.GetFieldValue<DateTimeOffset>(8));
}
