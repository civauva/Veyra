using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>Decides the severity of a database's long-query alert. Pure logic, tested without a database.</summary>
internal static class DatabaseLongQueryRule
{
    /// <summary>The window an alert must stay below caution before it clears.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Returns the severity the database's alert should have now.</summary>
    /// <param name="seconds">How long its longest running query has run now.</param>
    /// <param name="recentMax">The longest any query had run at any point of the last five minutes.</param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(
        double seconds,
        double recentMax,
        AlertSeverity? current,
        DatabaseLongQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var caution = options.CautionMinutes * 60;
        var raised = Thresholds.Rising(seconds, null, caution, options.WarningMinutes * 60, threshold => threshold);

        return Thresholds.HeldWhile(raised, current, recentMax >= caution);
    }

    /// <summary>Returns the seconds at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, DatabaseLongQueryOptions options) =>
        (severity == AlertSeverity.Warning ? options.WarningMinutes : options.CautionMinutes) * 60;
}
