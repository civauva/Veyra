namespace Veyra.Alerting.Incidents;

/// <summary>How alerts are grouped into incidents (ADR 0026), bound from <c>Veyra:Alerting:Incidents</c>.</summary>
public sealed class IncidentOptions
{
    /// <summary>
    /// How close together related alerts must open to share an incident, in minutes. Defaults to 15: long enough for
    /// a failure to spread from a database to the services that use it, short enough that tomorrow's failure of the
    /// same block is an incident of its own.
    /// </summary>
    public int GroupingMinutes { get; set; } = 15;
}
