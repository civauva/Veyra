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
            _ => new(
                settings.ErrorRate.CautionRatio,
                settings.ErrorRate.WarningRatio,
                settings.ErrorRate.CautionRatio / 2,
                settings.ErrorRate.Window),
        };
    }
}
