using Npgsql;
using Veyra.Testing;

namespace Veyra.Access.Tests;

/// <summary>
/// Creates migrated databases with the access schema, and seeds the rows tests need.
/// </summary>
internal static class AccessDatabase
{
    /// <summary>Creates a new database with the access schema applied.</summary>
    public static Task<NpgsqlDataSource> CreateAsync(PostgresFixture postgres) =>
        postgres.CreateMigratedDatabaseAsync(new AccessMigrations());

    /// <summary>Inserts an administrator directly, for tests that need a user to own something.</summary>
    public static async Task<Guid> SeedAdminAsync(NpgsqlDataSource database)
    {
        var id = Guid.CreateVersion7();

        await using var command = database.CreateCommand(
            """
            INSERT INTO access.user_account (id, email, email_normalized, display_name, role, created_at)
            VALUES ($1, 'admin@example.com', 'admin@example.com', 'Admin', 3, now())
            """);
        command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync();

        return id;
    }
}
