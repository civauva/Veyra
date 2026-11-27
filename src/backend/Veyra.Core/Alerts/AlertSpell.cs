namespace Veyra.Core.Alerts;

/// <summary>
/// A stretch of an alert's life at one severity, from the alert history (ADR 0012): it starts when the alert opened,
/// rose, or eased, and ends at its next change.
/// </summary>
/// <param name="AlertId">The alert.</param>
/// <param name="Environment">The subject's environment, or <see langword="null"/> when the sender set none.</param>
/// <param name="Subject">What the alert is about.</param>
/// <param name="Severity">The severity throughout the stretch.</param>
/// <param name="From">When the stretch started.</param>
/// <param name="To">When it ended; <see langword="null"/> while the alert is open at this severity.</param>
public sealed record AlertSpell(
    Guid AlertId,
    string? Environment,
    AlertSubject Subject,
    AlertSeverity Severity,
    DateTimeOffset From,
    DateTimeOffset? To);
