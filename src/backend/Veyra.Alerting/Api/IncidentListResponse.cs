namespace Veyra.Alerting.Api;

/// <summary>One page of incidents, open and acknowledged first, then the newest.</summary>
/// <param name="Items">The incidents.</param>
/// <param name="Next">The cursor for the next page; <see langword="null"/> on the last page.</param>
public sealed record IncidentListResponse(IReadOnlyList<IncidentItemResponse> Items, string? Next);
