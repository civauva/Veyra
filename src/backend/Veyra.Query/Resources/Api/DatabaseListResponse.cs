namespace Veyra.Query.Resources.Api;

/// <summary>One page of the Databases list (ADR 0022), worst first unless asked otherwise.</summary>
/// <param name="Header">The header strip, over every server the filters keep, not only this page.</param>
/// <param name="Items">The page's database servers.</param>
/// <param name="Total">How many servers the filters keep.</param>
/// <param name="Next">The cursor of the next page; null on the last page.</param>
public sealed record DatabaseListResponse(
    DatabaseListHeaderResponse Header,
    IReadOnlyList<DatabaseItemResponse> Items,
    int Total,
    string? Next);
