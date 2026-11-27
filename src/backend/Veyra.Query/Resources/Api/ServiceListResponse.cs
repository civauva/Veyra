namespace Veyra.Query.Resources.Api;

/// <summary>One page of the Services list (ADR 0022), worst first unless asked otherwise.</summary>
/// <param name="Header">The header strip, over every service the filters keep, not only this page.</param>
/// <param name="Items">The page's services.</param>
/// <param name="Total">How many services the filters keep.</param>
/// <param name="Next">The cursor of the next page; null on the last page.</param>
public sealed record ServiceListResponse(
    ServiceListHeaderResponse Header,
    IReadOnlyList<ServiceItemResponse> Items,
    int Total,
    string? Next);
