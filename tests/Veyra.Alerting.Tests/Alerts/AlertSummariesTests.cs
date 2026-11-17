using Veyra.Alerting.Alerts;
using Veyra.Alerting.Catalog;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.Alerts;

public class AlertSummariesTests
{
    private static readonly DateTimeOffset _from = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _now = new(2026, 11, 3, 7, 34, 0, TimeSpan.Zero);

    [Fact]
    public void Summarise_CountsOpenWarningsResolvedAndTheMedian()
    {
        var spans = new[]
        {
            Span(AlertRules.ErrorRate, AlertSeverity.Warning, AlertSeverity.Warning, _now.AddMinutes(-36), null),
            Span(AlertRules.DiskFilling, AlertSeverity.Caution, AlertSeverity.Caution, _now.AddHours(-4), null),
            Span(AlertRules.ErrorRate, AlertSeverity.Caution, AlertSeverity.Caution, _from.AddHours(3), 14),
            Span(AlertRules.LatencyP95, AlertSeverity.Caution, AlertSeverity.Warning, _from.AddHours(21), 25),
            Span(AlertRules.DiskSpace, AlertSeverity.Caution, AlertSeverity.Caution, _from.AddDays(1), 105),
        };

        var summary = AlertSummaries.Summarise(spans, _from, _now, new AlertingOptions());

        Assert.Equal(2, summary.Open);
        Assert.Equal(1, summary.OpenWarnings);
        Assert.Equal(5, summary.Total);
        Assert.Equal(2, summary.Warnings);
        Assert.Equal(3, summary.Resolved);
        Assert.Equal(25 * 60, summary.MedianSeconds);
        Assert.Equal(
            [
                (AlertRules.ErrorRate, 2),
                (AlertRules.LatencyP95, 1),
                (AlertRules.DiskSpace, 1),
                (AlertRules.DiskFilling, 1),
                (AlertRules.ContainerRestarts, 0),
                (AlertRules.ContainerMemory, 0),
                (AlertRules.DatabaseBlocking, 0),
                (AlertRules.DatabaseDeadlocks, 0),
                (AlertRules.DatabaseLongQuery, 0),
                (AlertRules.DatabaseBackupAge, 0),
                (AlertRules.DatabaseAgentJob, 0),
                (AlertRules.DatabaseConnections, 0),
                (AlertRules.DatabaseWraparound, 0),
                .. RuleCatalog.All.Select(rule => (rule.Id, 0)),
            ],
            summary.Rules.Select(rule => (rule.Rule, rule.Count)));
    }

    [Fact]
    public void Summarise_ListsEachRuleWithItsSettings()
    {
        var options = new AlertingOptions();
        options.Latency.Enabled = false;
        options.DiskFilling.CautionDays = 10;

        var summary = AlertSummaries.Summarise([], _from, _now, options);

        var latency = summary.Rules.Single(rule => rule.Rule == AlertRules.LatencyP95);
        var filling = summary.Rules.Single(rule => rule.Rule == AlertRules.DiskFilling);
        Assert.Equal(
            (false, 1.0, 3.0, 300),
            (latency.Enabled, latency.CautionThreshold, latency.WarningThreshold, latency.WindowSeconds));
        Assert.Equal(
            (true, 10.0, 3.0, 86400),
            (filling.Enabled, filling.CautionThreshold, filling.WarningThreshold, filling.WindowSeconds));
    }

    [Fact]
    public void Summarise_HasEveryDayWithItsCountAndWorstSeverity()
    {
        var spans = new[]
        {
            Span(AlertRules.ErrorRate, AlertSeverity.Caution, AlertSeverity.Caution, _from.AddHours(3), 14),
            Span(AlertRules.LatencyP95, AlertSeverity.Caution, AlertSeverity.Warning, _from.AddHours(21), 25),
            Span(AlertRules.ErrorRate, AlertSeverity.Warning, AlertSeverity.Warning, _now.AddMinutes(-36), null),
        };

        var summary = AlertSummaries.Summarise(spans, _from, _now, new AlertingOptions());

        Assert.Equal(
            [
                (new DateOnly(2026, 11, 1), 2, (AlertSeverity?)AlertSeverity.Warning),
                (new DateOnly(2026, 11, 2), 0, null),
                (new DateOnly(2026, 11, 3), 1, AlertSeverity.Warning),
            ],
            summary.Days.Select(day => (day.Date, day.Count, day.Worst)));
    }

    [Fact]
    public void Summarise_AlertOpenedBeforeThePeriod_CountsOnItsFirstDay()
    {
        var spans = new[]
        {
            Span(AlertRules.DiskSpace, AlertSeverity.Caution, AlertSeverity.Caution, _from.AddDays(-5), null),
        };

        var summary = AlertSummaries.Summarise(spans, _from, _now, new AlertingOptions());

        Assert.Equal(1, summary.Days[0].Count);
        Assert.Null(summary.MedianSeconds);
    }

    [Fact]
    public void Summarise_TimeZone_CountsEachAlertOnItsLocalDay()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var from = new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero);
        var spans = new[]
        {
            // 23:30 UTC on Nov 1 is 00:30 on Nov 2 in Berlin.
            Span(
                AlertRules.ErrorRate,
                AlertSeverity.Caution,
                AlertSeverity.Caution,
                new(2026, 11, 1, 23, 30, 0, TimeSpan.Zero),
                10),
        };

        var summary = AlertSummaries.Summarise(spans, from, _now, new AlertingOptions(), berlin);

        Assert.Equal(
            [(new DateOnly(2026, 11, 1), 0), (new DateOnly(2026, 11, 2), 1), (new DateOnly(2026, 11, 3), 0)],
            summary.Days.Select(day => (day.Date, day.Count)));
    }

    private static AlertSpan Span(
        string rule,
        AlertSeverity severity,
        AlertSeverity worst,
        DateTimeOffset openedAt,
        int? minutes) =>
        new(rule, severity, worst, openedAt, minutes is { } lasted ? openedAt.AddMinutes(lasted) : null);
}
