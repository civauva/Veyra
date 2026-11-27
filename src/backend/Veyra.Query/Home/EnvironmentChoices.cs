namespace Veyra.Query.Home;

/// <summary>
/// The lines of the environment switcher's list for what its find box holds (VEY-67): the choice of every environment
/// together, and each environment by name.
/// </summary>
internal static class EnvironmentChoices
{
    // What the choice of every environment is called on screen, so the find box finds it too.
    private const string _allLabel = "All environments";

    /// <summary>Builds the list.</summary>
    /// <param name="environments">Every environment seen in the last hour or with an open alert, by name.</param>
    /// <param name="search">Text an environment's name must hold, in any case; null or blank for every one.</param>
    /// <returns>The lines.</returns>
    public static EnvironmentChoicesResponse Build(IReadOnlyList<HomeEnvironmentResponse> environments, string? search)
    {
        ArgumentNullException.ThrowIfNull(environments);

        var needle = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        bool Holds(string name) => needle is null || name.Contains(needle, StringComparison.OrdinalIgnoreCase);

        // Every environment together carries the worst weather of any, so a storm elsewhere is never hidden.
        var all = new EnvironmentAllResponse(
            environments.Select(one => one.Condition).DefaultIfEmpty(HomeConditionKind.Clear).Max());

        return new EnvironmentChoicesResponse(
            Holds(_allLabel) ? all : null,
            [.. environments.Where(one => Holds(one.Name))]);
    }
}
