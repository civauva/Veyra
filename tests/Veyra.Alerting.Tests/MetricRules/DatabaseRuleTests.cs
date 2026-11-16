using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.MetricRules;

public class DatabaseRuleTests
{
    private static readonly DatabaseBlockingOptions _blocking = new();
    private static readonly DatabaseDeadlocksOptions _deadlocks = new();
    private static readonly DatabaseLongQueryOptions _longQuery = new();
    private static readonly DatabaseBackupAgeOptions _backupAge = new();

    [Theory]
    [InlineData(0, 0, null, null)]
    [InlineData(1, 3, null, AlertSeverity.Caution)]
    [InlineData(5, 8, null, AlertSeverity.Warning)]
    [InlineData(0, 2, null, null)]
    [InlineData(0, 2, AlertSeverity.Warning, AlertSeverity.Warning)]
    [InlineData(1, 1, AlertSeverity.Warning, AlertSeverity.Warning)]
    [InlineData(0, 0, AlertSeverity.Caution, null)]
    public void Blocking_LeastRaisesAndAnyBlockedSessionHolds(
        double least,
        double most,
        AlertSeverity? current,
        AlertSeverity? expected)
    {
        Assert.Equal(expected, DatabaseBlockingRule.Evaluate(least, most, current, _blocking));
    }

    [Theory]
    [InlineData(0, 0, null, null)]
    [InlineData(1, 1, null, AlertSeverity.Caution)]
    [InlineData(6, 6, null, AlertSeverity.Warning)]
    [InlineData(0, 1, AlertSeverity.Caution, AlertSeverity.Caution)]
    [InlineData(0, 0, AlertSeverity.Warning, null)]
    [InlineData(0, 3, null, null)]
    public void Deadlocks_FifteenMinutesRaiseAndThirtyMinutesWithoutOneClear(
        double quarter,
        double halfHour,
        AlertSeverity? current,
        AlertSeverity? expected)
    {
        Assert.Equal(expected, DatabaseDeadlocksRule.Evaluate(quarter, halfHour, current, _deadlocks));
    }

    [Theory]
    [InlineData(60, 60, null, null)]
    [InlineData(301, 301, null, AlertSeverity.Caution)]
    [InlineData(1800, 1800, null, AlertSeverity.Warning)]
    [InlineData(0, 1900, AlertSeverity.Warning, AlertSeverity.Warning)]
    [InlineData(0, 200, AlertSeverity.Caution, null)]
    public void LongQuery_RaisesFromNowAndHoldsWhileTheLastFiveMinutesReachedCaution(
        double seconds,
        double recentMax,
        AlertSeverity? current,
        AlertSeverity? expected)
    {
        Assert.Equal(expected, DatabaseLongQueryRule.Evaluate(seconds, recentMax, current, _longQuery));
    }

    [Theory]
    [InlineData(25, null, null)]
    [InlineData(26, null, AlertSeverity.Caution)]
    [InlineData(51, AlertSeverity.Caution, AlertSeverity.Warning)]
    [InlineData(0.2, AlertSeverity.Warning, null)]
    public void BackupAge_ClearsWithANewBackup(double hours, AlertSeverity? current, AlertSeverity? expected)
    {
        Assert.Equal(expected, DatabaseBackupAgeRule.Evaluate(hours * 3600, current, _backupAge));
    }

    [Theory]
    [InlineData("shop", true)]
    [InlineData("Scratch", false)]
    [InlineData("staging_copy", false)]
    public void BackupAge_IgnoredDatabases_AreNotJudged(string database, bool judged)
    {
        var options = new DatabaseBackupAgeOptions { IgnoredDatabases = "scratch, staging_copy" };

        Assert.Equal(judged, DatabaseBackupAgeRule.Judges(database, options));
    }

    [Fact]
    public void AgentJob_FailedRunIsACautionUntilTheNextRunSucceeds()
    {
        Assert.Equal(AlertSeverity.Caution, DatabaseAgentJobRule.Evaluate(failed: true));
        Assert.Null(DatabaseAgentJobRule.Evaluate(failed: false));
    }
}
