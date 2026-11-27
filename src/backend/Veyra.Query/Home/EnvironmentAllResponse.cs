namespace Veyra.Query.Home;

/// <summary>The switcher's choice of every environment together.</summary>
/// <param name="Condition">The worst weather of any environment.</param>
public sealed record EnvironmentAllResponse(HomeConditionKind Condition);
