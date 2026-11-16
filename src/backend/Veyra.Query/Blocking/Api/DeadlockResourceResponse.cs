namespace Veyra.Query.Blocking.Api;

/// <summary>One locked resource of a deadlock, with who held it and who waited (ADR 0020, area 5).</summary>
/// <param name="Kind">What is locked, such as a key, a page, or an object.</param>
/// <param name="Target">The table and index, when the graph names them.</param>
/// <param name="Holders">The processes that held it, with their lock mode.</param>
/// <param name="Waiters">The processes that waited for it, with the mode they asked for.</param>
public sealed record DeadlockResourceResponse(
    string Kind,
    string Target,
    IReadOnlyList<string> Holders,
    IReadOnlyList<string> Waiters);
