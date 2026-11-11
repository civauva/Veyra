namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in disk-filling rule, bound from the <c>Veyra:Alerting:DiskFilling</c> section (ADR 0012).
/// </summary>
/// <remarks>
/// The rule raises an alert per disk of a host that is full soon at the pace of the last day. An open alert clears
/// when the forecast moves past its threshold times <see cref="ClearFactor"/>, or when the disk stops filling.
/// </remarks>
public sealed class DiskFillingOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>A disk full within this many days raises a caution. Defaults to 14.</summary>
    public double CautionDays { get; set; } = 14;

    /// <summary>A disk full within this many days raises a warning. Defaults to 3.</summary>
    public double WarningDays { get; set; } = 3;

    /// <summary>
    /// How much later than its threshold the forecast must move before a held severity ends. Defaults to 1.5, so a
    /// caution clears beyond 21 days.
    /// </summary>
    public double ClearFactor { get; set; } = 1.5;
}
