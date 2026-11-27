namespace Veyra.Query.Home;

/// <summary>The environment switcher's list for what its find box holds (VEY-67).</summary>
/// <param name="All">The choice of every environment together; null when the search leaves it out.</param>
/// <param name="Environments">The environments whose names hold the search, by name.</param>
public sealed record EnvironmentChoicesResponse(
    EnvironmentAllResponse? All,
    IReadOnlyList<HomeEnvironmentResponse> Environments);
