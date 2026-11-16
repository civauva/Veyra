using Veyra.Alerting.Api;
using Veyra.Alerting.Catalog;
using Veyra.Core.Alerts;
using Veyra.Core.Time;

namespace Veyra.Alerting.Alerts;

/// <summary>Counts the alerts of a period for the Alerts screen. Pure logic, tested without a database.</summary>
internal static class AlertSummaries
{
    /// <summary>Summarises the alerts that were open at some time in a period.</summary>
    /// <remarks>Days are the viewer's days (VEY-22): an alert counts on the local day it opened.</remarks>
    /// <param name="spans">The alerts.</param>
    /// <param name="from">The start of the period, at midnight in <paramref name="zone"/>.</param>
    /// <param name="now">The end of the period.</param>
    /// <param name="settings">The Alerting settings, for each rule's thresholds.</param>
    /// <param name="zone">The viewer's time zone; UTC when not given.</param>
    /// <returns>The summary.</returns>
    public static AlertSummaryResponse Summarise(
        IReadOnlyCollection<AlertSpan> spans,
        DateTimeOffset from,
        DateTimeOffset now,
        AlertingOptions settings,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(settings);

        var durations = spans
            .Where(span => span.ResolvedAt is not null)
            .Select(span => (span.ResolvedAt!.Value - span.OpenedAt).TotalSeconds)
            .Order()
            .ToList();

        var days = zone ?? TimeZoneInfo.Utc;
        var first = TimeZones.LocalDay(from, days);
        var last = TimeZones.LocalDay(now, days);
        var byDay = spans
            .GroupBy(span => Max(TimeZones.LocalDay(span.OpenedAt, days), first))
            .ToDictionary(group => group.Key, group => group.ToList());
        var list = new List<AlertDayResponse>();

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var opened = byDay.GetValueOrDefault(day) ?? [];

            list.Add(new AlertDayResponse(
                day,
                opened.Count,
                opened.Count == 0 ? null : opened.Max(span => span.Worst)));
        }

        return new AlertSummaryResponse(
            From: from,
            To: now,
            Open: spans.Count(span => span.ResolvedAt is null),
            OpenWarnings: spans.Count(span => span.ResolvedAt is null && span.Severity == AlertSeverity.Warning),
            Total: spans.Count,
            Warnings: spans.Count(span => span.Worst == AlertSeverity.Warning),
            Resolved: durations.Count,
            MedianSeconds: Median(durations),
            Rules: AlertRules.All
                .Select(rule =>
                {
                    var thresholds = RuleSettings.For(rule, settings);

                    return new RuleCountResponse(
                        rule,
                        spans.Count(span => span.Rule == rule),
                        Enabled(rule, settings),
                        thresholds.Caution,
                        thresholds.Warning,
                        (int)thresholds.Window.TotalSeconds);
                })
                .ToList(),
            Days: list);
    }

    private static bool Enabled(string rule, AlertingOptions settings) => rule switch
    {
        AlertRules.DiskSpace => settings.DiskSpace.Enabled,
        AlertRules.DiskFilling => settings.DiskFilling.Enabled,
        AlertRules.LatencyP95 => settings.Latency.Enabled,
        AlertRules.DatabaseBlocking => settings.DatabaseBlocking.Enabled,
        AlertRules.DatabaseDeadlocks => settings.DatabaseDeadlocks.Enabled,
        AlertRules.DatabaseLongQuery => settings.DatabaseLongQuery.Enabled,
        AlertRules.DatabaseBackupAge => settings.DatabaseBackupAge.Enabled,
        AlertRules.DatabaseAgentJob => settings.DatabaseAgentJob.Enabled,
        _ when RuleCatalog.Find(rule) is { } catalog => RuleCatalog.Enabled(catalog, settings),
        _ => settings.ErrorRate.Enabled,
    };

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static double? Median(List<double> sorted) => sorted.Count switch
    {
        0 => null,
        var count when count % 2 == 1 => sorted[count / 2],
        var count => (sorted[(count / 2) - 1] + sorted[count / 2]) / 2,
    };
}
