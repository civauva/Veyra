namespace Veyra.Query.Home;

/// <summary>
/// One environment pill in the top bar.
/// </summary>
/// <param name="Name">The environment name, from <c>deployment.environment.name</c>.</param>
/// <param name="Condition">Its weather, from its open alerts.</param>
/// <param name="ErrorRatio">The share of its records that are errors right now, or null without records.</param>
public sealed record HomeEnvironmentResponse(string Name, HomeConditionKind Condition, double? ErrorRatio);
