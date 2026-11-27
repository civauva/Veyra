using Veyra.Alerting.Alerts;
using Veyra.Alerting.Incidents;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Api;

/// <summary>One incident in the list of the Incidents screen, and at the top of its own screen (ADR 0026).</summary>
/// <param name="Id">The incident's ID.</param>
/// <param name="Number">Its number.</param>
/// <param name="Key">Its key, such as <c>INC-42</c>.</param>
/// <param name="Title">The headline of its first alert, or of the worst one that joined later.</param>
/// <param name="Environment">The environment of its alerts, if they name one.</param>
/// <param name="Severity">The worst severity of its alerts.</param>
/// <param name="State"><c>open</c>, <c>acknowledged</c>, or <c>resolved</c>.</param>
/// <param name="OpenedAt">When its first alert opened.</param>
/// <param name="AcknowledgedAt">When someone acknowledged it; <see langword="null"/> when nobody has.</param>
/// <param name="AcknowledgedBy">Who acknowledged it, by name.</param>
/// <param name="ResolvedAt">When its last alert resolved; <see langword="null"/> while any is open.</param>
/// <param name="AlertCount">How many alerts it groups.</param>
/// <param name="Blocks">
/// The blocks its alerts are about, each once, in the order their alerts joined: whole blocks, so the part is only a
/// container's host.
/// </param>
public sealed record IncidentItemResponse(
    Guid Id,
    long Number,
    string Key,
    string Title,
    string? Environment,
    AlertSeverity Severity,
    string State,
    DateTimeOffset OpenedAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy,
    DateTimeOffset? ResolvedAt,
    int AlertCount,
    IReadOnlyList<AlertBlockResponse> Blocks)
{
    /// <summary>The item of a stored incident.</summary>
    /// <param name="incident">The incident.</param>
    /// <param name="alerts">Its alerts, in the order they joined.</param>
    /// <returns>The item.</returns>
    internal static IncidentItemResponse Of(Incident incident, IReadOnlyList<AlertListEntry> alerts) => new(
        incident.Id,
        incident.Number,
        incident.Key,
        incident.Title,
        incident.Environment,
        incident.Severity,
        IncidentStates.NameOf(incident.Status),
        incident.OpenedAt,
        incident.AcknowledgedAt,
        incident.AcknowledgedByName,
        incident.ResolvedAt,
        alerts.Count,
        [.. alerts.Select(entry => Whole(AlertBlockResponse.Of(entry.Alert))).Distinct()]);

    // A block as a whole: two disks of one host are one affected block. A container keeps its host, which tells
    // containers of one name apart (ADR 0018).
    private static AlertBlockResponse Whole(AlertBlockResponse block) =>
        block.Kind == "container" ? block : block with { Part = null };
}
