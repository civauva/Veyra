using Veyra.Alerting.Catalog;
using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.MetricRules;

/// <summary>
/// The Backups tab judges freshness with the backup rules' own thresholds (ADR 0025). These prove the Alerting module
/// hands out exactly what its rules use, as saved on the Settings screen.
/// </summary>
public class BackupRuleThresholdsTests
{
    [Fact]
    public void Of_Defaults_AreTheRulesDefaults()
    {
        var thresholds = BackupRuleThresholds.Of(new AlertingOptions());

        Assert.Equal(BackupThresholds.Default with { IgnoredDatabases = thresholds.IgnoredDatabases }, thresholds);
        Assert.Empty(thresholds.IgnoredDatabases);
    }

    [Fact]
    public void Of_SavedSettings_AreInSecondsWithTheIgnoredDatabases()
    {
        var settings = new AlertingOptions
        {
            DatabaseBackupAge = new DatabaseBackupAgeOptions
            {
                CautionHours = 30,
                WarningHours = 72,
                IgnoredDatabases = "scratch, Staging ,",
            },
        };
        settings.Catalog["LogBackupAge"] = new CatalogRuleOptions { Enabled = false, Caution = 30, Warning = 120 };

        var thresholds = BackupRuleThresholds.Of(settings);

        Assert.Equal((true, 30d * 3600, 72d * 3600), (
            thresholds.FullJudged,
            thresholds.FullCautionSeconds,
            thresholds.FullWarningSeconds));
        Assert.Equal(["scratch", "Staging"], thresholds.IgnoredDatabases);
        Assert.True(thresholds.Ignores("STAGING"));
        Assert.Equal((false, 1800d, 7200d), (
            thresholds.LogJudged,
            thresholds.LogCautionSeconds,
            thresholds.LogWarningSeconds));
    }
}
