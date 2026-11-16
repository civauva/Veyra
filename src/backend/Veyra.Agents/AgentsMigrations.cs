using Veyra.Core.Schema;

namespace Veyra.Agents;

/// <summary>
/// The schema scripts of the Agents module, embedded from its <c>Migrations</c> folder. They create and change the
/// <c>agents</c> schema.
/// </summary>
internal sealed class AgentsMigrations : ISchemaMigrationSource
{
    /// <summary>The module name, which is also the PostgreSQL schema name.</summary>
    public const string Module = "agents";

    /// <summary>Returns the embedded scripts, ordered by version.</summary>
    /// <returns>The Agents module's migrations.</returns>
    public IReadOnlyList<SchemaMigration> GetMigrations() =>
        SchemaMigrationScripts.FromAssembly(typeof(AgentsMigrations).Assembly, Module);
}
