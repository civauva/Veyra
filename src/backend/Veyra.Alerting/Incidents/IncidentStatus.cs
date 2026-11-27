namespace Veyra.Alerting.Incidents;

/// <summary>Where an incident stands (ADR 0026). Stored and answered by its name in lower case.</summary>
internal enum IncidentStatus
{
    /// <summary>At least one of its alerts is open and nobody acknowledged it.</summary>
    Open,

    /// <summary>At least one of its alerts is open and someone said they are on it.</summary>
    Acknowledged,

    /// <summary>Every one of its alerts resolved.</summary>
    Resolved,
}
