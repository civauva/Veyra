using Veyra.Core.Schema;

namespace Veyra.Storage;

/// <summary>
/// The schema scripts of the Storage module, embedded from its <c>Migrations</c> folder. They create and change the
/// telemetry tables in the <c>storage</c> schema.
/// </summary>
internal sealed class StorageMigrations : ISchemaMigrationSource
{
    /// <summary>The module name, which is also the PostgreSQL schema name.</summary>
    public const string Module = "storage";

    /// <summary>Returns the embedded scripts, ordered by version.</summary>
    /// <returns>The Storage module's migrations.</returns>
    public IReadOnlyList<SchemaMigration> GetMigrations() =>
        SchemaMigrationScripts.FromAssembly(typeof(StorageMigrations).Assembly, Module);
}
