using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Decides the severity of a PostgreSQL database's transaction ID wraparound alert. Pure logic, tested without a
/// database.
/// </summary>
internal static class DatabaseWraparoundRule
{
    /// <summary>The window the rule measures over: the age now, from the last five minutes.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Returns the severity the database's alert should have now.</summary>
    /// <param name="ratio">The age of its oldest unfrozen transaction ID of the limit, from 0 to 1.</param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(double ratio, AlertSeverity? current, DatabaseWraparoundOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A caution ends below the clear level; a warning eases to caution as far below its own level.
        var margin = options.CautionRatio - options.ClearRatio;

        return Thresholds.Rising(
            ratio,
            current,
            options.CautionRatio,
            options.WarningRatio,
            threshold => threshold - margin);
    }

    /// <summary>Returns the share of the limit at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, DatabaseWraparoundOptions options) =>
        severity == AlertSeverity.Warning ? options.WarningRatio : options.CautionRatio;
}
