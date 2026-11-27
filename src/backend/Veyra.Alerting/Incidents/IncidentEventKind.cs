namespace Veyra.Alerting.Incidents;

/// <summary>What someone did to an incident. The numbers are stored in <c>alerting.incident_event</c>.</summary>
internal enum IncidentEventKind
{
    /// <summary>Someone said they are on it.</summary>
    Acknowledged = 1,

    /// <summary>Someone took the acknowledgement back.</summary>
    Unacknowledged = 2,
}
