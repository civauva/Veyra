using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Decides the severity of a database server's blocking alert. Pure logic, tested without a database.
/// </summary>
internal static class DatabaseBlockingRule
{
    /// <summary>The window the rule measures over: the sessions blocked throughout the last five minutes.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Returns the severity the server's alert should have now.</summary>
    /// <param name="least">The fewest sessions blocked at any point of the last five minutes.</param>
    /// <param name="most">The most sessions blocked at any point of the last five minutes.</param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(
        double least,
        double most,
        AlertSeverity? current,
        DatabaseBlockingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var raised = Thresholds.Rising(
            least,
            null,
            options.CautionSessions,
            options.WarningSessions,
            threshold => threshold);

        return Thresholds.HeldWhile(raised, current, most > 0);
    }

    /// <summary>Returns the sessions at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, DatabaseBlockingOptions options) =>
        severity == AlertSeverity.Warning ? options.WarningSessions : options.CautionSessions;
}
