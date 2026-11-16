namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in blocking rule for database servers, bound from the
/// <c>Veyra:Alerting:DatabaseBlocking</c> section (ADR 0018).
/// </summary>
/// <remarks>
/// The rule raises an alert when sessions stay blocked for five minutes: the fewest blocked during those minutes
/// reach a level. It clears once no session was blocked for five minutes.
/// </remarks>
public sealed class DatabaseBlockingOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Sessions blocked throughout five minutes that raise a caution. Defaults to 1.</summary>
    public int CautionSessions { get; set; } = 1;

    /// <summary>Sessions blocked throughout five minutes that raise a warning. Defaults to 5.</summary>
    public int WarningSessions { get; set; } = 5;
}
