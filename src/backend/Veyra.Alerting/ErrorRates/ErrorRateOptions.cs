namespace Veyra.Alerting.ErrorRates;

/// <summary>
/// Settings of the built-in error-rate rule, bound from the <c>Veyra:Alerting:ErrorRate</c> section.
/// </summary>
/// <remarks>
/// The rule raises an alert per service when too many of its log records are errors. An open alert clears only when
/// the ratio falls below half of its threshold, so a ratio that hovers around a threshold does not flicker.
/// </remarks>
public sealed class ErrorRateOptions
{
    /// <summary>Whether the rule runs. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The time window the error ratio is measured over. Defaults to 5 minutes.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The fewest records a service must write in the window before the rule judges it. Fewer records leave its
    /// alert as it is, so one error in a quiet service does not raise one. Defaults to 20.
    /// </summary>
    public int MinimumRecords { get; set; } = 20;

    /// <summary>The error ratio, from 0 to 1, that raises a caution. Defaults to 0.01, which is 1 %.</summary>
    public double CautionRatio { get; set; } = 0.01;

    /// <summary>The error ratio, from 0 to 1, that raises a warning. Defaults to 0.05, which is 5 %.</summary>
    public double WarningRatio { get; set; } = 0.05;
}
