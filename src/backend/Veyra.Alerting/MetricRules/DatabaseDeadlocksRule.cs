using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Decides the severity of a database server's deadlock alert. Pure logic, tested without a database.
/// </summary>
internal static class DatabaseDeadlocksRule
{
    /// <summary>The window the rule counts over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>How long without a deadlock clears an alert.</summary>
    public static readonly TimeSpan ClearsAfter = TimeSpan.FromMinutes(30);

    /// <summary>Returns the severity the server's alert should have now.</summary>
    /// <param name="quarter">Deadlocks in the last 15 minutes.</param>
    /// <param name="halfHour">Deadlocks in the last 30 minutes.</param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(
        double quarter,
        double halfHour,
        AlertSeverity? current,
        DatabaseDeadlocksOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var raised = Thresholds.Rising(
            quarter,
            null,
            options.CautionDeadlocks,
            options.WarningDeadlocks,
            threshold => threshold);

        return Thresholds.HeldWhile(raised, current, halfHour >= 1);
    }

    /// <summary>Returns the deadlocks at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, DatabaseDeadlocksOptions options) =>
        severity == AlertSeverity.Warning ? options.WarningDeadlocks : options.CautionDeadlocks;
}
