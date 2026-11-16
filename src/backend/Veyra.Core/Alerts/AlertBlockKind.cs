namespace Veyra.Core.Alerts;

/// <summary>
/// The kind of block telemetry and alerts belong to (ADR 0007, ADR 0012, ADR 0018). Stored as text in
/// <c>alerting.alert.block_kind</c>, and as its number in the storage tables.
/// </summary>
public enum AlertBlockKind
{
    /// <summary>A service, named by its <c>service.name</c>.</summary>
    Service = 1,

    /// <summary>A host, named by its <c>host.name</c>.</summary>
    Host = 2,

    /// <summary>
    /// A database server, named by its engine and server, such as <c>microsoft.sql_server sql-prod-01</c>.
    /// </summary>
    Database = 3,

    /// <summary>A container, named by its <c>container.name</c>.</summary>
    Container = 4,
}
