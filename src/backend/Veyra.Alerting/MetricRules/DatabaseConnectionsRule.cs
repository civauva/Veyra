using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Decides the severity of a database server's connections alert. Pure logic, tested without a database.
/// </summary>
internal static class DatabaseConnectionsRule
{
    /// <summary>The window the rule measures over: the share in use now, from the last five minutes.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Returns the severity the server's alert should have now.</summary>
    /// <param name="ratio">The connections in use of the server's maximum, from 0 to 1.</param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(double ratio, AlertSeverity? current, DatabaseConnectionsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Thresholds.Rising(
            ratio,
            current,
            options.CautionRatio,
            options.WarningRatio,
            threshold => threshold - options.ClearMargin);
    }

    /// <summary>Returns the share of the maximum at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, DatabaseConnectionsOptions options) =>
        severity == AlertSeverity.Warning ? options.WarningRatio : options.CautionRatio;
}
