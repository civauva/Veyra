namespace Veyra.Alerting.Api;

/// <summary>One built-in rule as the installation sets it, and how many alerts it raised in a period.</summary>
/// <param name="Rule">The rule's name.</param>
/// <param name="Count">The alerts.</param>
/// <param name="Enabled">Whether the rule runs.</param>
/// <param name="CautionThreshold">Where it raises a caution, in its unit.</param>
/// <param name="WarningThreshold">Where it raises a warning, in its unit.</param>
/// <param name="WindowSeconds">The window it measures over.</param>
public sealed record RuleCountResponse(
    string Rule,
    int Count,
    bool Enabled,
    double CautionThreshold,
    double WarningThreshold,
    int WindowSeconds);
