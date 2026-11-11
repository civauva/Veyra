using Veyra.Alerting.Alerts;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Api;

/// <summary>One change of an alert, with the notifications it caused.</summary>
/// <param name="Kind">What changed.</param>
/// <param name="Severity">The severity after the change; for resolved, the severity it had last.</param>
/// <param name="Value">The measurement that caused it, as a ratio.</param>
/// <param name="At">When it happened.</param>
/// <param name="Deliveries">The messages about it, one per channel.</param>
public sealed record AlertEventResponse(
    AlertEventKind Kind,
    AlertSeverity Severity,
    double Value,
    DateTimeOffset At,
    IReadOnlyList<DeliveryResponse> Deliveries);
