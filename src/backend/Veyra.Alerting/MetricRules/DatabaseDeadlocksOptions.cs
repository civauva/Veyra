namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in deadlock rule for database servers, bound from the
/// <c>Veyra:Alerting:DatabaseDeadlocks</c> section (ADR 0018).
/// </summary>
/// <remarks>
/// The rule counts deadlocks over the last 15 minutes, and clears once there was none for 30 minutes.
/// </remarks>
public sealed class DatabaseDeadlocksOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Deadlocks in 15 minutes that raise a caution. Defaults to 1.</summary>
    public int CautionDeadlocks { get; set; } = 1;

    /// <summary>Deadlocks in 15 minutes that raise a warning. Defaults to 5.</summary>
    public int WarningDeadlocks { get; set; } = 5;
}
