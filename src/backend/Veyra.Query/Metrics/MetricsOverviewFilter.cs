using Veyra.Core.Metrics;

namespace Veyra.Query.Metrics;

/// <summary>
/// What the Metrics overview lists (VEY-67): the server applies it before reading, so a match is found however many
/// series there are.
/// </summary>
/// <param name="Search">
/// Text a card's name must hold, in any case: a host, service, container, or the server of a database; null or blank
/// for every card.
/// </param>
/// <param name="Environment">
/// The environment chosen in the top bar, or null for all. Hosts, database servers, and containers that name no
/// environment belong to every one; services must name it.
/// </param>
internal sealed record MetricsOverviewFilter(string? Search, string? Environment)
{
    /// <summary>Every card of every environment.</summary>
    public static MetricsOverviewFilter None { get; } = new(null, null);

    /// <summary>The search without its surrounding spaces, or null when it is blank.</summary>
    public string? Needle => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

    /// <summary>The environment, or null when it is blank.</summary>
    public string? Chosen => string.IsNullOrWhiteSpace(Environment) ? null : Environment.Trim();

    /// <summary>Whether it leaves any card out.</summary>
    public bool Filters => Needle is not null || Chosen is not null;

    /// <summary>Whether a card's name holds the search.</summary>
    /// <param name="name">The card's name.</param>
    /// <returns>True when it does, or when there is no search.</returns>
    public bool Matches(string name) =>
        Needle is null || name.Contains(Needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a card belongs to the chosen environment.</summary>
    /// <param name="environment">The environment the card's metrics name, if any.</param>
    /// <param name="everywhereWithout">
    /// True for hosts, database servers, and containers, which belong to every environment when they name none.
    /// </param>
    /// <returns>True when it does, or when no environment is chosen.</returns>
    public bool Holds(string? environment, bool everywhereWithout) =>
        Chosen is null || environment == Chosen || (everywhereWithout && environment is null);

    /// <summary>Narrows a series query to the search and environment, before its limit.</summary>
    /// <param name="query">The query of one kind of card.</param>
    /// <param name="everywhereWithout">Whether series that name no environment belong to every one.</param>
    /// <returns>The narrowed query.</returns>
    public MetricSeriesQuery Narrow(MetricSeriesQuery query, bool everywhereWithout) =>
        query with { BlockNameContains = Needle, Environment = Chosen, OrNoEnvironment = everywhereWithout };
}
