using Veyra.Alerting.Catalog;
using Veyra.Alerting.MetricRules;

namespace Veyra.Alerting.Alerts;

/// <summary>The thresholds of one built-in rule as the installation sets them, in the rule's unit.</summary>
/// <param name="Caution">Where a caution is raised.</param>
/// <param name="Warning">Where a warning is raised.</param>
/// <param name="Clear">Where a caution ends again.</param>
/// <param name="Window">The window the rule measures over.</param>
internal sealed record RuleSettings(double Caution, double Warning, double Clear, TimeSpan Window)
{
    /// <summary>Reads the thresholds of a rule from the Alerting settings.</summary>
    /// <param name="rule">The rule's name.</param>
    /// <param name="settings">The Alerting settings.</param>
    /// <returns>The thresholds; the error-rate rule's for an unknown name.</returns>
    public static RuleSettings For(string rule, AlertingOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return rule switch
        {
            AlertRules.DiskSpace => new(
                settings.DiskSpace.CautionRatio,
                settings.DiskSpace.WarningRatio,
                settings.DiskSpace.CautionRatio - settings.DiskSpace.ClearMargin,
                DiskSpaceRule.Window),
            AlertRules.DiskFilling => new(
                settings.DiskFilling.CautionDays,
                settings.DiskFilling.WarningDays,
                settings.DiskFilling.CautionDays * settings.DiskFilling.ClearFactor,
                DiskFillingRule.Window),
            AlertRules.LatencyP95 => new(
                settings.Latency.CautionSeconds,
                settings.Latency.WarningSeconds,
                settings.Latency.CautionSeconds * settings.Latency.ClearFactor,
                LatencyRule.Window),
            AlertRules.DatabaseBlocking => new(
                settings.DatabaseBlocking.CautionSessions,
                settings.DatabaseBlocking.WarningSessions,
                0,
                DatabaseBlockingRule.Window),
            AlertRules.DatabaseDeadlocks => new(
                settings.DatabaseDeadlocks.CautionDeadlocks,
                settings.DatabaseDeadlocks.WarningDeadlocks,
                0,
                DatabaseDeadlocksRule.Window),
            AlertRules.DatabaseLongQuery => new(
                settings.DatabaseLongQuery.CautionMinutes * 60,
                settings.DatabaseLongQuery.WarningMinutes * 60,
                settings.DatabaseLongQuery.CautionMinutes * 60,
                DatabaseLongQueryRule.Window),
            AlertRules.DatabaseBackupAge => new(
                settings.DatabaseBackupAge.CautionHours * 3600,
                settings.DatabaseBackupAge.WarningHours * 3600,
                settings.DatabaseBackupAge.CautionHours * 3600,
                DatabaseBackupAgeRule.Window),

            // A failed run is a caution only; the warning level equals it, so it is never crossed separately.
            AlertRules.DatabaseAgentJob => new(1, 1, 1, DatabaseAgentJobRule.Window),
            _ when RuleCatalog.Find(rule) is { } catalog => Catalog(catalog, settings),
            _ => new(
                settings.ErrorRate.CautionRatio,
                settings.ErrorRate.WarningRatio,
                settings.ErrorRate.CautionRatio / 2,
                settings.ErrorRate.Window),
        };
    }

    // A catalog rule raises and clears at its levels; one without a caution level uses its warning level for both.
    private static RuleSettings Catalog(CatalogRule rule, AlertingOptions settings)
    {
        var (caution, warning) = RuleCatalog.Levels(rule, settings);

        return new RuleSettings(caution ?? warning, warning, caution ?? warning, TimeSpan.Zero);
    }
}
