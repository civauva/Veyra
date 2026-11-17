namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in connections rule for database servers, bound from the
/// <c>Veyra:Alerting:DatabaseConnections</c> section (ADR 0018).
/// </summary>
/// <remarks>
/// The rule judges the connections in use of the server's maximum, which only PostgreSQL reports. Once the maximum is
/// reached, new connections are refused. An open alert clears only when the share falls <see cref="ClearMargin"/>
/// below its threshold.
/// </remarks>
public sealed class DatabaseConnectionsOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The share of the maximum in use, from 0 to 1, that raises a caution. Defaults to 0.80, 80 %.</summary>
    public double CautionRatio { get; set; } = 0.80;

    /// <summary>The share of the maximum in use, from 0 to 1, that raises a warning. Defaults to 0.90, 90 %.</summary>
    public double WarningRatio { get; set; } = 0.90;

    /// <summary>
    /// How far below its threshold the share must fall before a held severity ends. Defaults to 0.05, five points.
    /// </summary>
    public double ClearMargin { get; set; } = 0.05;
}
