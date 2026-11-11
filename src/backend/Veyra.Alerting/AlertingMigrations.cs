using Veyra.Core.Schema;

namespace Veyra.Alerting;

/// <summary>
/// The schema scripts of the Alerting module, embedded from its <c>Migrations</c> folder. They create and change the
/// <c>alerting</c> schema.
/// </summary>
internal sealed class AlertingMigrations : ISchemaMigrationSource
{
    /// <summary>The module name, which is also the PostgreSQL schema name.</summary>
    public const string Module = "alerting";

    /// <summary>Returns the embedded scripts, ordered by version.</summary>
    /// <returns>The Alerting module's migrations.</returns>
    public IReadOnlyList<SchemaMigration> GetMigrations() =>
        SchemaMigrationScripts.FromAssembly(typeof(AlertingMigrations).Assembly, Module);
}
