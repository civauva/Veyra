namespace Veyra.Alerting;

public sealed class AlertingOptions
{
    /// <summary>How often alert rules are evaluated against stored telemetry.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(30);
}
