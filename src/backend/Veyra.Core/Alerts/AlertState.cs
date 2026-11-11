namespace Veyra.Core.Alerts;

/// <summary>
/// One alert: a rule that fired for one subject, a service or a part of a host, from the moment it opened until it
/// resolved.
/// </summary>
/// <param name="Id">The alert's identity, stable for its whole life.</param>
/// <param name="Rule">The rule that raised it, for example <c>error-rate</c>.</param>
/// <param name="Environment">The subject's environment, or <see langword="null"/> when the sender set none.</param>
/// <param name="Subject">What the alert is about.</param>
/// <param name="Severity">The current severity. It can rise and fall while the alert stays open.</param>
/// <param name="Value">
/// The rule's latest measurement in the rule's unit: an error ratio from 0 to 1, a share of a disk in use, days until
/// a disk is full, or seconds of latency.
/// </param>
/// <param name="Peak">
/// The worst measurement since the alert opened: the highest, or for days until full the lowest.
/// </param>
/// <param name="Threshold">The threshold of the current severity, in the same unit as the value.</param>
/// <param name="Window">The time window the rule measures over.</param>
/// <param name="OpenedAt">When the rule first fired.</param>
/// <param name="UpdatedAt">When the rule last measured.</param>
/// <param name="ResolvedAt">When the alert resolved, or <see langword="null"/> while it is open.</param>
public sealed record AlertState(
    Guid Id,
    string Rule,
    string? Environment,
    AlertSubject Subject,
    AlertSeverity Severity,
    double Value,
    double Peak,
    double Threshold,
    TimeSpan Window,
    DateTimeOffset OpenedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt);
