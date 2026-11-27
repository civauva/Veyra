using Veyra.Query.Home;

namespace Veyra.Query.Resources;

/// <summary>
/// Orders the resource lists on the server (ADR 0022): worst first, or by name. Pure; tested without a database.
/// </summary>
internal static class ResourceRanking
{
    /// <summary>The order the lists start in: the ones that need a look first.</summary>
    public const string Worst = "worst";

    /// <summary>Alphabetically, whatever their health.</summary>
    public const string Name = "name";

    /// <summary>Whether a sort is one the lists know.</summary>
    /// <param name="sort">The sort asked for; null means worst first.</param>
    /// <returns><see langword="true"/> for <c>worst</c>, <c>name</c>, or none, in any case.</returns>
    public static bool IsSort(string? sort) =>
        sort is null
        || string.Equals(sort, Worst, StringComparison.OrdinalIgnoreCase)
        || string.Equals(sort, Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How bad an item is, the worst lowest: a warning, a caution, silent, unknown, then healthy. A silent item with an
    /// open alert ranks by its alert, since the alert says more.
    /// </summary>
    /// <param name="health">The item's health from its open alerts.</param>
    /// <param name="silent">Whether nothing arrived from it for longer than the setting allows.</param>
    /// <returns>0 for the worst, 4 for the healthiest.</returns>
    public static int Rank(BlockHealth health, bool silent) => health switch
    {
        BlockHealth.Warning => 0,
        BlockHealth.Caution => 1,
        _ when silent => 2,
        BlockHealth.Unknown => 3,
        _ => 4,
    };

    /// <summary>Sorts a list worst first or by name; names break ties, in any case and then exactly.</summary>
    /// <typeparam name="T">The item.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="sort"><c>worst</c> (also when null) or <c>name</c>.</param>
    /// <param name="name">The item's name.</param>
    /// <param name="health">The item's health.</param>
    /// <param name="silent">Whether the item is silent.</param>
    /// <returns>The sorted items.</returns>
    public static List<T> Sort<T>(
        IEnumerable<T> items,
        string? sort,
        Func<T, string> name,
        Func<T, BlockHealth> health,
        Func<T, bool> silent)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(silent);

        var byName = string.Equals(sort, Name, StringComparison.OrdinalIgnoreCase);
        var ordered = byName
            ? items.OrderBy(_ => 0)
            : items.OrderBy(item => Rank(health(item), silent(item)));

        return
        [
            .. ordered
                .ThenBy(name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name, StringComparer.Ordinal),
        ];
    }
}
