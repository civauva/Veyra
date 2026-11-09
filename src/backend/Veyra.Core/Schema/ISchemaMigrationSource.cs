namespace Veyra.Core.Schema;

/// <summary>
/// Supplies the schema migration scripts of one module.
/// </summary>
/// <remarks>
/// Each module that owns database objects registers one source. The storage module collects all sources and applies
/// their pending scripts at startup, so modules never reference the storage module directly.
/// </remarks>
public interface ISchemaMigrationSource
{
    /// <summary>
    /// Returns the module's scripts, ordered by version.
    /// </summary>
    /// <returns>The scripts of one module. The list may be empty.</returns>
    IReadOnlyList<SchemaMigration> GetMigrations();
}
