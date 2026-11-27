namespace Veyra.Alerting.Incidents;

/// <summary>One page of incidents, open and acknowledged first, then the newest.</summary>
/// <param name="Items">The incidents.</param>
/// <param name="Next">Where the next page starts; <see langword="null"/> on the last page.</param>
internal sealed record IncidentPage(IReadOnlyList<Incident> Items, IncidentCursor? Next);
