namespace Veyra.Query.Search;

/// <summary>
/// Orders names found by a search: exact matches first, then those that start with the text, then any others that
/// hold it, each by name (ADR 0022). Storage picks its candidates the same way; this decides the order the groups show.
/// </summary>
internal static class SearchRanking
{
    /// <summary>How well a name matches the text, ignoring case.</summary>
    /// <param name="name">The name.</param>
    /// <param name="text">The search text.</param>
    /// <returns>The rank.</returns>
    public static SearchRank RankOf(string name, string text)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(text);

        if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
        {
            return SearchRank.Exact;
        }

        if (name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
        {
            return SearchRank.Prefix;
        }

        return name.Contains(text, StringComparison.OrdinalIgnoreCase) ? SearchRank.Contains : SearchRank.None;
    }

    /// <summary>Keeps the items whose name holds the text, best first.</summary>
    /// <typeparam name="T">The kind of item.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="name">The name an item is found by.</param>
    /// <param name="text">The search text.</param>
    /// <returns>The matching items: by rank, then by name ignoring case, then by name exactly.</returns>
    public static IEnumerable<T> Rank<T>(IEnumerable<T> items, Func<T, string> name, string text) =>
        items
            .Select(item => (Item: item, Name: name(item), Rank: RankOf(name(item), text)))
            .Where(entry => entry.Rank != SearchRank.None)
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => entry.Item);
}
