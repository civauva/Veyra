namespace Veyra.Alerting.Api;

/// <summary>One page of alerts, newest first.</summary>
/// <param name="Items">The alerts.</param>
/// <param name="Next">The cursor for the next page; <see langword="null"/> on the last page.</param>
public sealed record AlertListResponse(IReadOnlyList<AlertListItemResponse> Items, string? Next);
