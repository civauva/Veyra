namespace Veyra.Query.Resources.Api;

/// <summary>One page of the Hosts and containers list (ADR 0022), worst first unless asked otherwise.</summary>
/// <param name="Header">The header strip, over every item the filters keep, not only this page.</param>
/// <param name="Items">The page's hosts and containers.</param>
/// <param name="Total">How many hosts and containers the filters keep.</param>
/// <param name="Next">The cursor of the next page; null on the last page.</param>
public sealed record HostListResponse(
    HostListHeaderResponse Header,
    IReadOnlyList<HostItemResponse> Items,
    int Total,
    string? Next);
