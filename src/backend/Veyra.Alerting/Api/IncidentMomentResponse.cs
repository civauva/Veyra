using Veyra.Core.Alerts;

namespace Veyra.Alerting.Api;

/// <summary>
/// One moment of an incident's own timeline: an event of one of its alerts with the notifications it sent, or an
/// acknowledgement and its taking back (ADR 0026).
/// </summary>
/// <param name="At">When it happened.</param>
/// <param name="Kind">
/// <c>opened</c>, <c>escalated</c>, <c>eased</c>, or <c>resolved</c> for an alert; <c>acknowledged</c> or
/// <c>unacknowledged</c> for the incident.
/// </param>
/// <param name="AlertId">The alert it happened to; <see langword="null"/> for an acknowledgement.</param>
/// <param name="Rule">That alert's rule.</param>
/// <param name="Block">That alert's block.</param>
/// <param name="Severity">The alert's severity after it.</param>
/// <param name="Value">The measurement that caused it, in the rule's unit.</param>
/// <param name="Actor">Who did it, for an acknowledgement.</param>
/// <param name="Deliveries">The notifications it sent, one per channel.</param>
public sealed record IncidentMomentResponse(
    DateTimeOffset At,
    string Kind,
    Guid? AlertId,
    string? Rule,
    AlertBlockResponse? Block,
    AlertSeverity? Severity,
    double? Value,
    string? Actor,
    IReadOnlyList<DeliveryResponse> Deliveries);
