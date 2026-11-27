namespace Veyra.Query.Incidents.Api;

/// <summary>
/// What surrounds one alert (ADR 0029): the blocks it touches and the logs around its opening. The parts are those of
/// the incident context, so both screens read the same shapes.
/// </summary>
/// <param name="From">The start of the window: an hour before the alert opened.</param>
/// <param name="To">Its end: when the alert resolved, or now.</param>
/// <param name="Logs">Warnings and worse from the components, newest first.</param>
/// <param name="Components">The alert's block, then the blocks related to it, each with its health now.</param>
public sealed record AlertContextResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IncidentLogsResponse Logs,
    IReadOnlyList<IncidentComponentResponse> Components);
