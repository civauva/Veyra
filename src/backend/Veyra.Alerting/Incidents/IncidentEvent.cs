namespace Veyra.Alerting.Incidents;

/// <summary>Something someone did to an incident, for its timeline.</summary>
/// <param name="Kind">What they did.</param>
/// <param name="ActorName">Who did it, by the name they had then.</param>
/// <param name="At">When.</param>
internal sealed record IncidentEvent(IncidentEventKind Kind, string ActorName, DateTimeOffset At);
