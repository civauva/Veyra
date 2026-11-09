namespace Veyra.Core.Security;

/// <summary>
/// The fixed roles of ADR 0008. Each role includes everything the role before it can do.
/// </summary>
/// <remarks>
/// The numeric values are stored in the database and compared with <c>&gt;=</c>, so their order and values must never
/// change.
/// </remarks>
public enum VeyraRole
{
    /// <summary>Sees all telemetry, dashboards, alerts, and incidents.</summary>
    Viewer = 1,

    /// <summary>Also manages alert rules, groupings, and dashboards, and acknowledges alerts.</summary>
    Editor = 2,

    /// <summary>Also manages users, roles, ingestion keys, and settings.</summary>
    Admin = 3,
}
