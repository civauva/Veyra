namespace Veyra.Query.Home;

/// <summary>
/// The headline of the home: the weather, where it is, and the error ratio behind it.
/// </summary>
/// <param name="Kind">Clear, showers, or storm.</param>
/// <param name="Over">
/// The group that holds alert 1, or null when clear. Every block is a service for now, so it is always Services.
/// </param>
/// <param name="ErrorRatio">The share of all records that are errors right now, or null without records.</param>
public sealed record HomeConditionResponse(HomeConditionKind Kind, string? Over, double? ErrorRatio);
