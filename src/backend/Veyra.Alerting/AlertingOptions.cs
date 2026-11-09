namespace Veyra.Alerting;

/// <summary>
/// Settings of the Alerting module, bound from the <c>Veyra:Alerting</c> section.
/// </summary>
public sealed class AlertingOptions
{
    /// <summary>How often alert rules are evaluated against stored telemetry.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(30);
}
