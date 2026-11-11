using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Turns a measurement into a severity with two thresholds and hysteresis: a severity is raised when its threshold is
/// reached, and once held it stays until the measurement is clearly past the threshold again, so a value that hovers
/// around a threshold does not open and close an alert every evaluation.
/// </summary>
internal static class Thresholds
{
    /// <summary>For measurements where higher is worse, such as a share of a disk in use.</summary>
    /// <param name="value">The measurement.</param>
    /// <param name="current">The severity held now, or <see langword="null"/> when no alert is open.</param>
    /// <param name="caution">The value from which a caution is raised.</param>
    /// <param name="warning">The value from which a warning is raised.</param>
    /// <param name="clearsBelow">The value below which a held severity with the given threshold ends.</param>
    /// <returns>The severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Rising(
        double value,
        AlertSeverity? current,
        double caution,
        double warning,
        Func<double, double> clearsBelow)
    {
        ArgumentNullException.ThrowIfNull(clearsBelow);

        bool Holds(AlertSeverity level, double threshold) =>
            value >= threshold || (current >= level && value >= clearsBelow(threshold));

        return Holds(AlertSeverity.Warning, warning) ? AlertSeverity.Warning
            : Holds(AlertSeverity.Caution, caution) ? AlertSeverity.Caution
            : null;
    }

    /// <summary>For measurements where lower is worse, such as days until a disk is full.</summary>
    /// <param name="value">The measurement.</param>
    /// <param name="current">The severity held now, or <see langword="null"/> when no alert is open.</param>
    /// <param name="caution">The value up to which a caution is raised.</param>
    /// <param name="warning">The value up to which a warning is raised.</param>
    /// <param name="clearsAbove">The value above which a held severity with the given threshold ends.</param>
    /// <returns>The severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Falling(
        double value,
        AlertSeverity? current,
        double caution,
        double warning,
        Func<double, double> clearsAbove)
    {
        ArgumentNullException.ThrowIfNull(clearsAbove);

        bool Holds(AlertSeverity level, double threshold) =>
            value <= threshold || (current >= level && value <= clearsAbove(threshold));

        return Holds(AlertSeverity.Warning, warning) ? AlertSeverity.Warning
            : Holds(AlertSeverity.Caution, caution) ? AlertSeverity.Caution
            : null;
    }
}
