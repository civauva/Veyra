using Veyra.Core.Alerts;

namespace Veyra.Alerting.ErrorRates;

/// <summary>
/// Decides the severity of one service's error-rate alert from its records in the window. Pure logic, so every case
/// can be tested without a database.
/// </summary>
internal static class ErrorRateRule
{
    /// <summary>The rule's name, stored with every alert it raises.</summary>
    public const string Name = "error-rate";

    /// <summary>
    /// Returns the severity the service's alert should have now.
    /// </summary>
    /// <remarks>
    /// <para>A ratio at or above a threshold raises that severity at once.</para>
    /// <para>
    /// A severity that is already held stays until the ratio falls below half of its threshold. This hysteresis
    /// keeps an alert from opening and closing every evaluation while the ratio hovers around a threshold.
    /// </para>
    /// <para>
    /// With fewer records than <see cref="ErrorRateOptions.MinimumRecords"/>, the rule cannot judge, so the current
    /// state is kept: no new alert opens, and an open one stays open.
    /// </para>
    /// </remarks>
    /// <param name="total">All records of the service in the window.</param>
    /// <param name="errors">Its records at error severity or worse.</param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(long total, long errors, AlertSeverity? current, ErrorRateOptions options)
    {
        if (total < options.MinimumRecords || total == 0)
        {
            return current;
        }

        var ratio = (double)errors / total;

        if (Holds(ratio, AlertSeverity.Warning, current, options))
        {
            return AlertSeverity.Warning;
        }

        if (Holds(ratio, AlertSeverity.Caution, current, options))
        {
            return AlertSeverity.Caution;
        }

        return null;
    }

    /// <summary>Returns the error ratio, from 0 to 1, at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, ErrorRateOptions options) =>
        severity == AlertSeverity.Warning ? options.WarningRatio : options.CautionRatio;

    // Reached outright, or already held at this level or above and not yet below half the threshold.
    private static bool Holds(double ratio, AlertSeverity level, AlertSeverity? current, ErrorRateOptions options)
    {
        var threshold = ThresholdFor(level, options);

        return ratio >= threshold || (current >= level && ratio >= threshold / 2);
    }
}
