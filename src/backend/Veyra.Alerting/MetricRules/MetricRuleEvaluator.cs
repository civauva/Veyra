using Microsoft.Extensions.Options;
using Veyra.Alerting.Alerts;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;

namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Runs the built-in metric rules once (ADR 0012): disk space and disk filling for every disk of every host, and
/// latency for every service that sends request durations.
/// </summary>
/// <remarks>
/// A subject the metrics no longer show, a host that stopped sending or a disk that was unmounted, keeps its open
/// alert as it is, as the error-rate rule does with a service that stopped logging. The alert resolves once the
/// metrics show the subject clear.
/// </remarks>
/// <param name="health">The disk and latency figures, from the Query module.</param>
/// <param name="store">Where alerts are kept.</param>
/// <param name="transitions">Applies the rules' verdicts to the alerts.</param>
/// <param name="options">The Alerting settings with the rules' thresholds.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class MetricRuleEvaluator(
    IMetricHealth health,
    AlertStore store,
    AlertTransitions transitions,
    IOptions<AlertingOptions> options,
    TimeProvider clock)
{
    /// <summary>Evaluates the rules as of now.</summary>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>A task that completes when every alert is up to date.</returns>
    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();

        if (settings.DiskSpace.Enabled || settings.DiskFilling.Enabled)
        {
            var disks = (await health.ListDisksAsync(now, cancellationToken))
                .GroupBy(disk => new Key(disk.Environment, AlertSubject.Host(disk.Host, disk.Mountpoint)))
                .ToDictionary(group => group.Key, group => group.First());

            if (settings.DiskSpace.Enabled)
            {
                await ApplyAsync(
                    AlertRules.DiskSpace,
                    disks,
                    (disk, current) => DiskSpace(disk, current, settings.DiskSpace),
                    now,
                    cancellationToken);
            }

            if (settings.DiskFilling.Enabled)
            {
                await ApplyAsync(
                    AlertRules.DiskFilling,
                    disks,
                    (disk, current) => DiskFilling(disk, current, settings.DiskFilling),
                    now,
                    cancellationToken);
            }
        }

        if (settings.Latency.Enabled)
        {
            var services = (await health.ListLatenciesAsync(now, cancellationToken))
                .GroupBy(latency => new Key(latency.Environment, AlertSubject.Service(latency.Service)))
                .ToDictionary(group => group.Key, group => group.First());

            await ApplyAsync(
                AlertRules.LatencyP95,
                services,
                (latency, current) => Latency(latency, current, settings.Latency),
                now,
                cancellationToken);
        }
    }

    // Every measured subject, and every open alert of the rule, whose subject may be measured or not.
    private async Task ApplyAsync<T>(
        string rule,
        IReadOnlyDictionary<Key, T> measured,
        Func<T, AlertState?, RuleVerdict> judge,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var open = (await store.ListOpenAsync(rule, cancellationToken))
            .GroupBy(alert => new Key(alert.Environment, alert.Subject))
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var (key, figures) in measured)
        {
            var current = open.GetValueOrDefault(key);

            await transitions.ApplyAsync(judge(figures, current), current, now, cancellationToken);
        }
    }

    private static RuleVerdict DiskSpace(DiskHealth disk, AlertState? current, DiskSpaceOptions rule)
    {
        var next = DiskSpaceRule.Evaluate(disk.Ratio, current?.Severity, rule);

        return new RuleVerdict(
            AlertRules.DiskSpace,
            disk.Environment,
            AlertSubject.Host(disk.Host, disk.Mountpoint),
            next,
            disk.Ratio,
            next is { } severity ? DiskSpaceRule.ThresholdFor(severity, rule) : 0,
            DiskSpaceRule.Window);
    }

    private static RuleVerdict DiskFilling(DiskHealth disk, AlertState? current, DiskFillingOptions rule)
    {
        var next = DiskFillingRule.Evaluate(disk.DaysUntilFull, current?.Severity, rule);

        return new RuleVerdict(
            AlertRules.DiskFilling,
            disk.Environment,
            AlertSubject.Host(disk.Host, disk.Mountpoint),
            next,
            disk.DaysUntilFull ?? DiskFillingRule.NotFilling,
            next is { } severity ? DiskFillingRule.ThresholdFor(severity, rule) : 0,
            DiskFillingRule.Window);
    }

    private static RuleVerdict Latency(ServiceLatency latency, AlertState? current, LatencyOptions rule)
    {
        var next = LatencyRule.Evaluate(latency.P95Seconds, latency.Requests, current?.Severity, rule);

        return new RuleVerdict(
            AlertRules.LatencyP95,
            latency.Environment,
            AlertSubject.Service(latency.Service),
            next,
            LatencyRule.Judges(latency.P95Seconds, latency.Requests, rule) ? latency.P95Seconds : null,
            next is { } severity ? LatencyRule.ThresholdFor(severity, rule) : 0,
            LatencyRule.Window);
    }

    private sealed record Key(string? Environment, AlertSubject Subject);
}
