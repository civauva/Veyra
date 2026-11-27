namespace Veyra.Query.Search.Api;

/// <summary>How many log records of the window hold every word of the search (ADR 0016).</summary>
/// <param name="Count">The records counted.</param>
/// <param name="Partial">
/// Whether counting ran out of its 150 ms; the count then covers the newest part of the window only.
/// </param>
public sealed record SearchCountResponse(long Count, bool Partial);
