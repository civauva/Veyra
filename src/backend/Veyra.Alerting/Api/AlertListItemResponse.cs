using Veyra.Alerting.Alerts;
using Veyra.Alerting.Catalog;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Api;

/// <summary>One alert in the list of the Alerts screen.</summary>
/// <param name="Id">The alert's ID.</param>
/// <param name="Rule">The rule that raised it.</param>
/// <param name="Environment">The environment, if the sender named one.</param>
/// <param name="Block">What it is about.</param>
/// <param name="Severity">The severity it has now, or had last when resolved.</param>
/// <param name="Worst">The highest severity it ever had.</param>
/// <param name="Value">The latest measurement, in the rule's unit.</param>
/// <param name="Peak">The worst measurement.</param>
/// <param name="PeakAt">When the peak was measured, if known.</param>
/// <param name="Threshold">The threshold of the current severity.</param>
/// <param name="WindowSeconds">The rule's measuring window.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="UpdatedAt">When it was last measured.</param>
/// <param name="ResolvedAt">When it resolved; <see langword="null"/> while open.</param>
/// <param name="Area">
/// For a database server, the area of its screen the alert belongs to, such as <c>blocking</c> (VEY-68); otherwise
/// <see langword="null"/>.
/// </param>
/// <param name="IncidentId">The incident the alert belongs to (ADR 0026); <see langword="null"/> for none.</param>
/// <param name="IncidentKey">That incident's key, such as <c>INC-42</c>.</param>
public sealed record AlertListItemResponse(
    Guid Id,
    string Rule,
    string? Environment,
    AlertBlockResponse Block,
    AlertSeverity Severity,
    AlertSeverity Worst,
    double Value,
    double Peak,
    DateTimeOffset? PeakAt,
    double Threshold,
    int WindowSeconds,
    DateTimeOffset OpenedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    string? Area,
    Guid? IncidentId = null,
    string? IncidentKey = null)
{
    /// <summary>The list entry of a stored alert.</summary>
    /// <param name="entry">The alert with its worst severity and peak time.</param>
    /// <returns>The entry.</returns>
    internal static AlertListItemResponse Of(AlertListEntry entry)
    {
        var alert = entry.Alert;

        return new AlertListItemResponse(
            alert.Id,
            alert.Rule,
            alert.Environment,
            AlertBlockResponse.Of(alert),
            alert.Severity,
            entry.Worst,
            alert.Value,
            alert.Peak,
            entry.PeakAt,
            alert.Threshold,
            (int)alert.Window.TotalSeconds,
            alert.OpenedAt,
            alert.UpdatedAt,
            alert.ResolvedAt,
            alert.Subject.Kind == AlertBlockKind.Database ? RuleAreas.Of(alert.Rule) : null,
            entry.IncidentId,
            entry.IncidentNumber is { } number ? IncidentState.KeyOf(number) : null);
    }
}
