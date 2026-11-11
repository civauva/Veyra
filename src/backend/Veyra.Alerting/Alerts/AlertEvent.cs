using Veyra.Core.Alerts;

namespace Veyra.Alerting.Alerts;

/// <summary>One change in the life of an alert, from <c>alerting.alert_event</c>.</summary>
/// <param name="Id">The event's ID.</param>
/// <param name="AlertId">The alert it belongs to.</param>
/// <param name="Kind">What changed.</param>
/// <param name="Severity">The severity after the change; for a resolved alert, the severity it had last.</param>
/// <param name="Value">The measurement that caused the change.</param>
/// <param name="At">When it changed.</param>
internal sealed record AlertEvent(
    long Id,
    Guid AlertId,
    AlertEventKind Kind,
    AlertSeverity Severity,
    double Value,
    DateTimeOffset At);
