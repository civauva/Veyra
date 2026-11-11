namespace Veyra.Alerting.Notifications;

/// <summary>The alert part of a notification, as receivers see it.</summary>
/// <param name="Id">The alert's ID.</param>
/// <param name="Rule">The rule that raised it, for example <c>error-rate</c> or <c>disk-filling</c>.</param>
/// <param name="Severity"><c>warning</c> or <c>caution</c>.</param>
/// <param name="Service">
/// The service it is about, for service alerts; <see langword="null"/> for host alerts. Kept beside
/// <paramref name="Block"/> so receivers written for the first version keep working (ADR 0012).
/// </param>
/// <param name="Block">What it is about: a service or a host, and the part of it, such as a disk.</param>
/// <param name="Environment">The environment, if the sender named one.</param>
/// <param name="Unit">
/// The unit of the measurements: <c>ratio</c> from 0 to 1, <c>days</c> until a disk is full, or <c>seconds</c>.
/// </param>
/// <param name="Value">The measurement that caused this notification.</param>
/// <param name="Threshold">The threshold of the current severity.</param>
/// <param name="Peak">The worst measurement so far.</param>
/// <param name="WindowSeconds">The rule's measuring window.</param>
/// <param name="OpenedAt">When the alert opened.</param>
/// <param name="ResolvedAt">When it resolved; <see langword="null"/> while it is open.</param>
/// <param name="Url">The alert's page in Veyra, when Veyra knows its public address.</param>
internal sealed record AlertNotice(
    Guid Id,
    string Rule,
    string Severity,
    string? Service,
    BlockNotice Block,
    string? Environment,
    string Unit,
    double Value,
    double Threshold,
    double Peak,
    int WindowSeconds,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt,
    Uri? Url);
