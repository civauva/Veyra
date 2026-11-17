using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>Decides the severity of a container's restart alert. Pure logic, tested without a database.</summary>
internal static class ContainerRestartsRule
{
    /// <summary>The window the rule counts over; as long without a restart clears an alert.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>Returns the severity the container's alert should have now.</summary>
    /// <param name="restarts">Restarts in the last 15 minutes.</param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(double restarts, AlertSeverity? current, ContainerRestartsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var raised = Thresholds.Rising(
            restarts,
            null,
            options.CautionRestarts,
            options.WarningRestarts,
            threshold => threshold);

        // A container that restarted at all within the window has not settled yet, so its alert holds.
        return Thresholds.HeldWhile(raised, current, restarts >= 1);
    }

    /// <summary>Returns the restarts at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, ContainerRestartsOptions options) =>
        severity == AlertSeverity.Warning ? options.WarningRestarts : options.CautionRestarts;
}
