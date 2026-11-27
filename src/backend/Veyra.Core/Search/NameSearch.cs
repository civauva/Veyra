namespace Veyra.Core.Search;

/// <summary>What to look for by name.</summary>
/// <param name="Text">Text the name must hold, in any case. Not empty.</param>
/// <param name="From">The start of the window, inclusive.</param>
/// <param name="To">The end of the window, exclusive.</param>
/// <param name="Environment">
/// Only this environment, or <see langword="null"/> for all. Hosts, containers, and database servers that name no
/// environment belong to every one.
/// </param>
/// <param name="Limit">The most to return.</param>
public sealed record NameSearch(
    string Text,
    DateTimeOffset From,
    DateTimeOffset To,
    string? Environment,
    int Limit);
