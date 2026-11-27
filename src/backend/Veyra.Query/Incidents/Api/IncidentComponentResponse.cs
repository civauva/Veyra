namespace Veyra.Query.Incidents.Api;

/// <summary>
/// A block the incident affects or one related to it, so the screen tells what failed from what only ran nearby
/// (ADR 0026).
/// </summary>
/// <param name="Block">The block.</param>
/// <param name="Health"><c>ok</c>, <c>caution</c>, <c>warning</c>, or <c>silent</c>, now.</param>
/// <param name="InIncident">Whether it has an open alert of the incident.</param>
/// <param name="Relation">
/// <c>affected</c> when an alert of the incident is about it, <c>host</c> when an affected block runs on it, or
/// <c>runs-on-host</c> when it runs on an affected host.
/// </param>
public sealed record IncidentComponentResponse(
    IncidentBlockResponse Block,
    string Health,
    bool InIncident,
    string Relation);
