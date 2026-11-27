namespace Veyra.Query.Search.Api;

/// <summary>One group of a search's answer.</summary>
/// <typeparam name="T">The kind of item.</typeparam>
/// <param name="Items">The best matches, best first.</param>
/// <param name="Partial">
/// Whether the group ran out of its 150 ms before it finished, so it holds only what it had found by then.
/// </param>
public sealed record SearchGroupResponse<T>(IReadOnlyList<T> Items, bool Partial);
