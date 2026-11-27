using Veyra.Alerting.Alerts;

namespace Veyra.Alerting.Incidents;

/// <summary>An alert of an incident, as the alert list shows it.</summary>
/// <param name="IncidentId">The incident it belongs to.</param>
/// <param name="Entry">The alert with its worst severity and peak time.</param>
internal sealed record IncidentAlertEntry(Guid IncidentId, AlertListEntry Entry);
