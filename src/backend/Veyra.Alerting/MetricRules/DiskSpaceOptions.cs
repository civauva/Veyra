namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in disk-space rule, bound from the <c>Veyra:Alerting:DiskSpace</c> section (ADR 0012).
/// </summary>
/// <remarks>
/// The rule raises an alert per disk of a host when too much of it is in use. An open alert clears only when the
/// share falls <see cref="ClearMargin"/> below its threshold.
/// </remarks>
public sealed class DiskSpaceOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The share in use, from 0 to 1, that raises a caution. Defaults to 0.90, which is 90 %.</summary>
    public double CautionRatio { get; set; } = 0.90;

    /// <summary>The share in use, from 0 to 1, that raises a warning. Defaults to 0.95, which is 95 %.</summary>
    public double WarningRatio { get; set; } = 0.95;

    /// <summary>
    /// How far below its threshold the share must fall before a held severity ends. Defaults to 0.05, five points.
    /// </summary>
    public double ClearMargin { get; set; } = 0.05;
}
