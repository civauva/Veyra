using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>Decides the severity of a database's backup-age alert. Pure logic, tested without a database.</summary>
internal static class DatabaseBackupAgeRule
{
    /// <summary>The window the rule measures over: the age reported in the last five minutes.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Returns the severity the database's alert should have now.</summary>
    /// <param name="seconds">Seconds since its last full backup.</param>
    /// <param name="current">The severity of its open alert, or <see langword="null"/> when none is open.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The new severity, or <see langword="null"/> when no alert should be open.</returns>
    public static AlertSeverity? Evaluate(double seconds, AlertSeverity? current, DatabaseBackupAgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A new backup brings the age down at once, so no margin is needed against flapping.
        return Thresholds.Rising(
            seconds,
            current,
            options.CautionHours * 3600,
            options.WarningHours * 3600,
            threshold => threshold);
    }

    /// <summary>Whether the rule judges a database: not one of those not backed up on purpose.</summary>
    /// <param name="database">The database name.</param>
    /// <param name="options">The settings with the ignored databases.</param>
    /// <returns><see langword="true"/> when the database is judged.</returns>
    public static bool Judges(string database, DatabaseBackupAgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !(options.IgnoredDatabases ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(database, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns the seconds at which a severity is raised.</summary>
    /// <param name="severity">The severity.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The threshold.</returns>
    public static double ThresholdFor(AlertSeverity severity, DatabaseBackupAgeOptions options) =>
        (severity == AlertSeverity.Warning ? options.WarningHours : options.CautionHours) * 3600;
}
