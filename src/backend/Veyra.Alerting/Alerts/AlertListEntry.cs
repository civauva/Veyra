using Veyra.Core.Alerts;

namespace Veyra.Alerting.Alerts;

/// <summary>One alert in a page of the Alerts screen.</summary>
/// <param name="Alert">The alert.</param>
/// <param name="Worst">The highest severity it ever had.</param>
/// <param name="PeakAt">When its peak was measured; unknown for alerts opened before Veyra recorded it.</param>
/// <param name="IncidentId">
/// The incident it belongs to (ADR 0026); <see langword="null"/> for alerts opened before Veyra had incidents.
/// </param>
/// <param name="IncidentNumber">That incident's number.</param>
internal sealed record AlertListEntry(
    AlertState Alert,
    AlertSeverity Worst,
    DateTimeOffset? PeakAt,
    Guid? IncidentId = null,
    long? IncidentNumber = null);
