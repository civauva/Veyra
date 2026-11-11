using Npgsql;
using Veyra.Testing;

namespace Veyra.Alerting.Tests;

/// <summary>Creates migrated databases with the alerting schema.</summary>
internal static class AlertingDatabase
{
    /// <summary>Creates a new database with the alerting schema applied.</summary>
    public static Task<NpgsqlDataSource> CreateAsync(PostgresFixture postgres) =>
        postgres.CreateMigratedDatabaseAsync(new AlertingMigrations());
}
