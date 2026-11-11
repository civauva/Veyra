using Veyra.Core.Alerts;

namespace Veyra.Query.Home;

/// <summary>
/// One alert in the Needs you pane, or in the list of alerts resolved recently.
/// </summary>
/// <param name="N">
/// The number of the alert in the Needs you pane, from 1, which dashboards draw as a badge. Null when resolved.
/// </param>
/// <param name="Id">The identity of the alert.</param>
/// <param name="Block">The ID of the block the alert is about.</param>
/// <param name="Service">The service name, for service alerts; null for host alerts.</param>
/// <param name="Kind">What the alert is about: <c>service</c> or <c>host</c> (ADR 0012).</param>
/// <param name="Name">The service or host name.</param>
/// <param name="Part">The part of the block, for example a disk's mount point; null for none.</param>
/// <param name="Environment">The environment, or null when the sender set none.</param>
/// <param name="Rule">The rule that raised it, for example <c>error-rate</c>.</param>
/// <param name="Severity">The current severity, or the last one for a resolved alert.</param>
/// <param name="Value">The latest measurement, for example an error ratio from 0 to 1.</param>
/// <param name="Peak">The highest measurement while it was open.</param>
/// <param name="Threshold">The threshold of its severity.</param>
/// <param name="WindowSeconds">The window the rule measures over, in seconds.</param>
/// <param name="Since">When it opened.</param>
/// <param name="ResolvedAt">When it resolved, or null while it is open.</param>
public sealed record HomeAlertResponse(
    int? N,
    Guid Id,
    string Block,
    string? Service,
    string Kind,
    string Name,
    string? Part,
    string? Environment,
    string Rule,
    AlertSeverity Severity,
    double Value,
    double Peak,
    double Threshold,
    int WindowSeconds,
    DateTimeOffset Since,
    DateTimeOffset? ResolvedAt);
