using Veyra.Alerting.Alerts;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Notifications;

/// <summary>A delivery the dispatcher has claimed, with everything needed to send it.</summary>
/// <param name="Id">The delivery's ID.</param>
/// <param name="Attempt">The number of this try, from 1.</param>
/// <param name="Event">The alert event it reports.</param>
/// <param name="Alert">The alert as it is now.</param>
/// <param name="Channel">The channel it goes to.</param>
/// <param name="IncidentId">The incident of the alert, so the message links it (ADR 0026); null for none.</param>
/// <param name="IncidentNumber">That incident's number.</param>
internal sealed record DueDelivery(
    long Id,
    int Attempt,
    AlertEvent Event,
    AlertState Alert,
    NotificationChannel Channel,
    Guid? IncidentId = null,
    long? IncidentNumber = null);
