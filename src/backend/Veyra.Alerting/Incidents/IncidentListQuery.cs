namespace Veyra.Alerting.Incidents;

/// <summary>Which incidents the Incidents screen lists.</summary>
/// <param name="From">The start of the period: incidents open at some time from then on.</param>
/// <param name="To">The end of the period, usually now.</param>
/// <param name="Limit">The most incidents on the page.</param>
/// <param name="Status">Only incidents in this state; <see langword="null"/> for every state.</param>
/// <param name="Environment">Only incidents of this environment; <see langword="null"/> for every one.</param>
/// <param name="After">Where the previous page ended; <see langword="null"/> for the first page.</param>
internal sealed record IncidentListQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    int Limit,
    IncidentStatus? Status = null,
    string? Environment = null,
    IncidentCursor? After = null);
