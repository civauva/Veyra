using Veyra.Query.Metrics;

namespace Veyra.Query.Resources;

/// <summary>What every resource list is asked for (ADR 0022); the controller has checked each part.</summary>
/// <param name="Environment">
/// The environment chosen in the top bar, or null for all. Hosts, containers, and database servers that name none
/// belong to every one; services must name it.
/// </param>
/// <param name="Span">The window the figures cover.</param>
/// <param name="Health">The health chips chosen.</param>
/// <param name="Search">Text an item's name must hold, in any case; null for every item.</param>
/// <param name="Sort"><c>worst</c> (also when null) or <c>name</c>.</param>
/// <param name="Cursor">The cursor of the previous page; null for the first.</param>
/// <param name="Limit">The most items on the page.</param>
internal sealed record ResourceListQuery(
    string? Environment,
    MetricSpan Span,
    ResourceHealthFilter Health,
    string? Search,
    string? Sort,
    string? Cursor,
    int Limit)
{
    /// <summary>The environment, or null when it is blank.</summary>
    public string? Chosen => string.IsNullOrWhiteSpace(Environment) ? null : Environment.Trim();

    /// <summary>The search without its surrounding spaces, or null when it is blank.</summary>
    public string? Needle => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

    /// <summary>Whether a name holds the search.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when it does, or when there is no search.</returns>
    public bool Matches(string name) =>
        Needle is null || name.Contains(Needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether an item belongs to the chosen environment.</summary>
    /// <param name="environment">The item's environment, if any.</param>
    /// <param name="everywhereWithout">Whether an item that names none belongs to every environment.</param>
    /// <returns><see langword="true"/> when it does, or when no environment is chosen.</returns>
    public bool Holds(string? environment, bool everywhereWithout) =>
        Chosen is null || environment == Chosen || (everywhereWithout && environment is null);
}
