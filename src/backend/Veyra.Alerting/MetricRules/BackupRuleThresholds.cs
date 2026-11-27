using Microsoft.Extensions.Options;
using Veyra.Alerting.Catalog;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Hands the backup rules' thresholds to other modules (ADR 0025): the full backup age rule's own settings and the log
/// backup age rule of the catalog, read the way the rules read them, so the Backups tab agrees with the alerts.
/// </summary>
/// <param name="options">The Alerting settings, current after every save.</param>
internal sealed class BackupRuleThresholds(IOptionsMonitor<AlertingOptions> options) : IBackupThresholds
{
    /// <summary>The catalog rule that judges log backups.</summary>
    public const string LogRule = "db-log-backup-age";

    /// <inheritdoc />
    public BackupThresholds Current => Of(options.CurrentValue);

    /// <summary>Reads the thresholds from the settings. Pure; tested without a host.</summary>
    /// <param name="settings">The Alerting settings.</param>
    /// <returns>The thresholds, in seconds.</returns>
    public static BackupThresholds Of(AlertingOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var full = settings.DatabaseBackupAge;
        var log = RuleCatalog.Find(LogRule)
            ?? throw new InvalidOperationException($"The catalog has no rule {LogRule}.");
        var (logCaution, logWarning) = RuleCatalog.Levels(log, settings);
        var ignored = (full.IgnoredDatabases ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new BackupThresholds(
            full.Enabled,
            full.CautionHours * 3600,
            full.WarningHours * 3600,
            ignored,
            RuleCatalog.Enabled(log, settings),
            logCaution,
            logWarning);
    }
}
