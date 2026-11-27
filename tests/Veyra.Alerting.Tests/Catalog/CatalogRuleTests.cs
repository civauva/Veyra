using Veyra.Alerting.Alerts;
using Veyra.Alerting.Catalog;
using Veyra.Alerting.MetricRules;
using Veyra.Alerting.Notifications;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Telemetry;

namespace Veyra.Alerting.Tests.Catalog;

/// <summary>
/// Catalog rules judge alike and word themselves from their entry (ADR 0020). These tests prove the judging at the
/// levels, a rule where lower is worse, the levels from settings, and the words of values and headlines.
/// </summary>
public class CatalogRuleTests
{
    private static readonly TelemetryBlock _server = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");

    [Theory]
    [InlineData(30, null)]
    [InlineData(60, AlertSeverity.Caution)]
    [InlineData(299, AlertSeverity.Caution)]
    [InlineData(300, AlertSeverity.Warning)]
    public void Judge_HigherIsWorse_RaisesAtEachLevel(double value, AlertSeverity? expected)
    {
        var rule = RuleCatalog.Find("db-head-blocker")!;

        var verdict = MetricRuleEvaluator.Judge(rule, Measure(value), 60, 300);

        Assert.Equal(expected, verdict.Severity);
        Assert.Equal(value, verdict.Value);

        if (expected is not null)
        {
            Assert.Equal(expected == AlertSeverity.Warning ? 300 : 60, verdict.Threshold);
        }
    }

    [Fact]
    public void Judge_LowerIsWorseAndNoCautionLevel_RaisesOnlyTheWarningBelowIt()
    {
        var rule = RuleCatalog.Find("db-head-blocker")! with { LowerIsWorse = true };

        Assert.Equal(AlertSeverity.Warning, MetricRuleEvaluator.Judge(rule, Measure(5), null, 10).Severity);
        Assert.Null(MetricRuleEvaluator.Judge(rule, Measure(11), null, 10).Severity);
    }

    [Fact]
    public void Levels_SettingsInTheScreensUnit_AreTurnedIntoTheMeasuresUnit()
    {
        var rule = RuleCatalog.Find("db-open-transaction")!;
        var options = new AlertingOptions();

        Assert.Equal((600d, 1800d), RuleCatalog.Levels(rule, options));

        options.Catalog["openTransaction"] = new CatalogRuleOptions { Warning = 45 };

        Assert.Equal((600d, 2700d), RuleCatalog.Levels(rule, options));
        Assert.True(RuleCatalog.Enabled(rule, options));
    }

    [Fact]
    public void CatalogRules_AreAmongTheBuiltInRulesWithTheirSettings()
    {
        Assert.Contains("db-head-blocker", AlertRules.All);
        Assert.Equal(new RuleSettings(60, 300, 60, TimeSpan.Zero), RuleSettings.For("db-head-blocker", new()));
        Assert.Equal(new RuleSettings(0.25, 0.5, 0.25, TimeSpan.Zero), RuleSettings.For("db-tempdb-session", new()));
    }

    [Theory]
    [InlineData("db-head-blocker", 90, "90 s")]
    [InlineData("db-head-blocker", 134, "2 min")]
    [InlineData("db-open-transaction", 2_000, "33 min")]
    [InlineData("db-open-transaction", 5_400, "1 h 30 min")]
    [InlineData("db-tempdb-session", 0.4, "40%")]
    public void Measure_CatalogRule_IsWordedInItsUnit(string rule, double value, string expected)
    {
        Assert.Equal(expected, NotificationMessages.Measure(rule, value));
    }

    [Fact]
    public void Fill_Headline_NamesTheServerAndValue()
    {
        var rule = RuleCatalog.Find("db-head-blocker")!;

        Assert.Equal(
            "SQL Server sql-prod-01: requests have waited 3 min behind another session",
            CatalogWords.Fill(rule.Headline, "SQL Server sql-prod-01", null, CatalogWords.Value(rule, 180)));
    }

    [Fact]
    public void Guidance_EveryCatalogRule_SaysWhatItMeansAndHowToFixIt()
    {
        foreach (var rule in RuleCatalog.All)
        {
            var guidance = RuleGuidance.For(rule.Id);

            Assert.False(string.IsNullOrWhiteSpace(guidance.Meaning), $"{rule.Id} says nothing of what it means");
            Assert.NotEmpty(guidance.Steps);
            Assert.All(guidance.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.Text), rule.Id));

            // Screens fill in only the server and the part; any other brace would be shown as it is.
            var words = string.Join(" ", guidance.Steps.SelectMany(step => new[] { step.Text, step.Script ?? "" }));
            var left = words
                .Replace("{server}", "", StringComparison.Ordinal)
                .Replace("{part}", "", StringComparison.Ordinal);
            Assert.DoesNotContain('{', left);
        }
    }

    [Fact]
    public void Guidance_IntegrityCheckAge_GivesTheCheckToRunOnTheDatabase()
    {
        var steps = RuleGuidance.For("db-checkdb-age").Steps;

        Assert.Contains(steps, step => step.Script?.Contains("DBCC CHECKDB (N'{part}')", StringComparison.Ordinal) == true);
    }

    private static DatabaseMeasure Measure(double value) =>
        new(_server, "production", "head-blocker-seconds", null, value);
}
