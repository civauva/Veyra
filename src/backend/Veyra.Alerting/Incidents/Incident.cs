using Veyra.Core.Alerts;

namespace Veyra.Alerting.Incidents;

/// <summary>One incident, from <c>alerting.incident</c> (ADR 0026).</summary>
/// <param name="Id">The incident's ID.</param>
/// <param name="Number">Its number, which people say: INC-42.</param>
/// <param name="Environment">The environment of its alerts, or <see langword="null"/> when they name none.</param>
/// <param name="Title">The headline of its first alert, or of the worst one that joined later.</param>
/// <param name="Severity">The worst severity of its alerts.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="OpenedAt">When its first alert opened.</param>
/// <param name="AcknowledgedAt">When someone acknowledged it; <see langword="null"/> when nobody has.</param>
/// <param name="AcknowledgedById">The user who acknowledged it.</param>
/// <param name="AcknowledgedByName">That user's name as it was then.</param>
/// <param name="ResolvedAt">When its last alert resolved; <see langword="null"/> while any is open.</param>
/// <param name="UpdatedAt">When it last changed.</param>
internal sealed record Incident(
    Guid Id,
    long Number,
    string? Environment,
    string Title,
    AlertSeverity Severity,
    IncidentStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? AcknowledgedAt,
    Guid? AcknowledgedById,
    string? AcknowledgedByName,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>The key people see and say, such as <c>INC-42</c>.</summary>
    public string Key => IncidentState.KeyOf(Number);
}
