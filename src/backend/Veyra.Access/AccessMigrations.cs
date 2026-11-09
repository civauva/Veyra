using Veyra.Core.Schema;

namespace Veyra.Access;

/// <summary>
/// The schema scripts of the Access module, embedded from its <c>Migrations</c> folder. They create and change the
/// <c>access</c> schema.
/// </summary>
internal sealed class AccessMigrations : ISchemaMigrationSource
{
    /// <summary>The module name, which is also the PostgreSQL schema name.</summary>
    public const string Module = "access";

    /// <summary>Returns the embedded scripts, ordered by version.</summary>
    /// <returns>The Access module's migrations.</returns>
    public IReadOnlyList<SchemaMigration> GetMigrations() =>
        SchemaMigrationScripts.FromAssembly(typeof(AccessMigrations).Assembly, Module);
}
