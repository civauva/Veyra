namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in SQL Agent job rule, bound from the <c>Veyra:Alerting:DatabaseAgentJob</c> section
/// (ADR 0018).
/// </summary>
/// <remarks>
/// The rule raises a caution per job whose last run failed, and clears when the next run succeeds.
/// </remarks>
public sealed class DatabaseAgentJobOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;
}
