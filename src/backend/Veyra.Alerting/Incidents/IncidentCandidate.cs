using Veyra.Core.Alerts;

namespace Veyra.Alerting.Incidents;

/// <summary>An alert of an open incident, which a new alert may join when their blocks are related.</summary>
/// <param name="IncidentId">The incident.</param>
/// <param name="IncidentSeverity">The incident's severity, the worst of its alerts.</param>
/// <param name="Subject">What the alert is about.</param>
/// <param name="OpenedAt">When the alert opened.</param>
internal sealed record IncidentCandidate(
    Guid IncidentId,
    AlertSeverity IncidentSeverity,
    AlertSubject Subject,
    DateTimeOffset OpenedAt);
