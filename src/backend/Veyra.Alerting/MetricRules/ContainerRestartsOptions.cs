namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in restart rule for Docker containers, bound from the
/// <c>Veyra:Alerting:ContainerRestarts</c> section (ADR 0018).
/// </summary>
/// <remarks>
/// The rule counts restarts over the last 15 minutes, and clears once there was none for 15 minutes.
/// </remarks>
public sealed class ContainerRestartsOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Restarts in 15 minutes that raise a caution. Defaults to 2.</summary>
    public int CautionRestarts { get; set; } = 2;

    /// <summary>Restarts in 15 minutes that raise a warning. Defaults to 5.</summary>
    public int WarningRestarts { get; set; } = 5;
}
