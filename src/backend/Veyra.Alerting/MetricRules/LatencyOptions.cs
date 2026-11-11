namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Settings of the built-in latency rule, bound from the <c>Veyra:Alerting:Latency</c> section (ADR 0012).
/// </summary>
/// <remarks>
/// The rule raises an alert per service when the 95th percentile of its HTTP request durations over the last five
/// minutes is too high. An open alert clears when it falls below its threshold times <see cref="ClearFactor"/>.
/// </remarks>
public sealed class LatencyOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The 95th percentile, in seconds, that raises a caution. Defaults to 1.</summary>
    public double CautionSeconds { get; set; } = 1;

    /// <summary>The 95th percentile, in seconds, that raises a warning. Defaults to 3.</summary>
    public double WarningSeconds { get; set; } = 3;

    /// <summary>
    /// The share of its threshold the percentile must fall below before a held severity ends. Defaults to 0.8.
    /// </summary>
    public double ClearFactor { get; set; } = 0.8;

    /// <summary>
    /// The fewest requests a service must answer in five minutes before the rule judges it. Fewer leave its alert as
    /// it is, so one slow request in a quiet service does not raise one. Defaults to 20.
    /// </summary>
    public int MinimumRequests { get; set; } = 20;
}
