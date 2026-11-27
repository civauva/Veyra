namespace Veyra.Access.Api;

/// <summary>The saved views of a screen a person sees: their own first, then those others shared, each by name.</summary>
/// <param name="Items">The views.</param>
public sealed record SavedViewListResponse(IReadOnlyList<SavedViewResponse> Items);
