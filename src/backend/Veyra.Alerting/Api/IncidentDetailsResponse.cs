namespace Veyra.Alerting.Api;

/// <summary>An incident with its alerts, figures, timeline, and runbooks, for its screen (ADR 0026).</summary>
/// <param name="Incident">The incident.</param>
/// <param name="Alerts">Its alerts, as the alert list shows them, in the order they joined.</param>
/// <param name="Figures">When it was first detected, how long it lasted, and what it affects.</param>
/// <param name="Timeline">Its alerts' events and its acknowledgements, oldest first.</param>
/// <param name="Runbooks">The runbook of each of its rules that has one.</param>
public sealed record IncidentDetailsResponse(
    IncidentItemResponse Incident,
    IReadOnlyList<AlertListItemResponse> Alerts,
    IncidentFiguresResponse Figures,
    IReadOnlyList<IncidentMomentResponse> Timeline,
    IReadOnlyList<RunbookResponse> Runbooks);
