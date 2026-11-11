using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>Decides the severity of one disk's disk-filling alert. Pure logic, tested without a database.</summary>
internal static class DiskFillingRule
{
    /// <summary>
    /// The value recorded for a disk that is not filling, or not within a year: the forecasts the screens leave out.
    /// </summary>
    public const double NotFilling = 365;

    /// <summary>The window the rule measures over: the pace of the last day.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(1);

    /// <summary>Returns the severity the disk's alert should have now.</summary>
    /// <param name="daysUntilFull">
    /// When the disk is full at the pace of the last day; <see langword="null"/> when it is not filling.
    /// </param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(double? daysUntilFull, AlertSeverity? current, DiskFillingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (daysUntilFull is not { } days)
        {
            return null;
        }

        return Thresholds.Falling(
            days,
            current,
            options.CautionDays,
            options.WarningDays,
            threshold => threshold * options.ClearFactor);
    }

    /// <summary>Returns the days until full at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, DiskFillingOptions options) =>
        severity == AlertSeverity.Warning ? options.WarningDays : options.CautionDays;
}
