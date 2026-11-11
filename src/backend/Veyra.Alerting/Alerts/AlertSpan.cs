using Veyra.Core.Alerts;

namespace Veyra.Alerting.Alerts;

/// <summary>
/// An alert reduced to what the Alerts screen counts: its rule, how bad it is and got, and how long it lasted.
/// </summary>
/// <param name="Rule">The rule that raised it.</param>
/// <param name="Severity">The severity it has now, or had last when resolved.</param>
/// <param name="Worst">The highest severity it ever had.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="ResolvedAt">When it resolved; <see langword="null"/> while open.</param>
internal sealed record AlertSpan(
    string Rule,
    AlertSeverity Severity,
    AlertSeverity Worst,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt);
