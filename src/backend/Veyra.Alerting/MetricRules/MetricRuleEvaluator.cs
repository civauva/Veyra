using Microsoft.Extensions.Options;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Catalog;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;

namespace Veyra.Alerting.MetricRules;

/// <summary>
/// Runs the built-in metric rules once (ADR 0012, ADR 0018): disk space and disk filling for every disk of every
/// host, latency for every service that sends request durations, and blocking, deadlocks, long queries, backup age,
/// and SQL Agent jobs for every database server.
/// </summary>
/// <remarks>
/// A subject the metrics no longer show, a host that stopped sending or a disk that was unmounted, keeps its open
/// alert as it is, as the error-rate rule does with a service that stopped logging. The alert resolves once the
/// metrics show the subject clear.
/// </remarks>
/// <param name="health">The disk, latency, and database figures, from the Query module.</param>
/// <param name="store">Where alerts are kept.</param>
/// <param name="transitions">Applies the rules' verdicts to the alerts.</param>
/// <param name="options">The Alerting settings with the rules' thresholds.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class MetricRuleEvaluator(
    IMetricHealth health,
    AlertStore store,
    AlertTransitions transitions,
    IOptionsMonitor<AlertingOptions> options,
    TimeProvider clock)
{
    /// <summary>Evaluates the rules as of now.</summary>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>A task that completes when every alert is up to date.</returns>
    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
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

        if (DatabaseRulesOn(settings))
        {
            await EvaluateDatabasesAsync(settings, now, cancellationToken);
        }

        await EvaluateCatalogAsync(settings, now, cancellationToken);
    }

    /// <summary>
    /// Judges a catalog rule's measure of one subject (ADR 0020). Pure logic, tested without a database.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <param name="measure">The subject's measure.</param>
    /// <param name="caution">The caution level, or <see langword="null"/> when the rule has none.</param>
    /// <param name="warning">The warning level.</param>
    /// <returns>What the rule decided.</returns>
    public static RuleVerdict Judge(CatalogRule rule, DatabaseMeasure measure, double? caution, double warning)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(measure);

        bool Reaches(double level) => rule.LowerIsWorse ? measure.Value <= level : measure.Value >= level;

        AlertSeverity? severity = Reaches(warning)
            ? AlertSeverity.Warning
            : caution is { } level && Reaches(level) ? AlertSeverity.Caution : null;

        return new RuleVerdict(
            rule.Id,
            measure.Environment,
            AlertSubject.Database(measure.Server.Name, measure.Part),
            severity,
            measure.Value,
            severity == AlertSeverity.Caution ? caution!.Value : warning,
            TimeSpan.Zero);
    }

    // Every catalog rule over the figures the Query module measured, read once for all rules.
    private async Task EvaluateCatalogAsync(
        AlertingOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rules = RuleCatalog.All.Where(rule => RuleCatalog.Enabled(rule, settings)).ToList();

        if (rules.Count == 0)
        {
            return;
        }

        var measures = await health.ListDatabaseMeasuresAsync(now, cancellationToken);

        foreach (var rule in rules)
        {
            var (caution, warning) = RuleCatalog.Levels(rule, settings);

            await ApplyAsync(
                rule.Id,
                Unique(measures
                    .Where(measure => measure.Measure == rule.Measure)
                    .Select(measure => (
                        new Key(measure.Environment, AlertSubject.Database(measure.Server.Name, measure.Part)),
                        measure))),
                (measure, _) => Judge(rule, measure, caution, warning),
                now,
                cancellationToken);
        }
    }

    private static bool DatabaseRulesOn(AlertingOptions settings) =>
        settings.DatabaseBlocking.Enabled
        || settings.DatabaseDeadlocks.Enabled
        || settings.DatabaseLongQuery.Enabled
        || settings.DatabaseBackupAge.Enabled
        || settings.DatabaseAgentJob.Enabled;

    // The five database rules over every server that sends, each server's figures read once.
    private async Task EvaluateDatabasesAsync(
        AlertingOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var servers = await health.ListDatabasesAsync(now, cancellationToken);

        Key Of(DatabaseHealth server, string? part = null) =>
            new(server.Environment, AlertSubject.Database(server.Server.Name, part));

        if (settings.DatabaseBlocking.Enabled)
        {
            await ApplyAsync(
                AlertRules.DatabaseBlocking,
                Unique(servers.Where(server => server.BlockedLeast is not null).Select(server => (Of(server), server))),
                (server, current) => Blocking(server, current, settings.DatabaseBlocking),
                now,
                cancellationToken);
        }

        if (settings.DatabaseDeadlocks.Enabled)
        {
            await ApplyAsync(
                AlertRules.DatabaseDeadlocks,
                Unique(servers
                    .Where(server => server.DeadlocksQuarter is not null)
                    .Select(server => (Of(server), server))),
                (server, current) => Deadlocks(server, current, settings.DatabaseDeadlocks),
                now,
                cancellationToken);
        }

        if (settings.DatabaseLongQuery.Enabled)
        {
            await ApplyAsync(
                AlertRules.DatabaseLongQuery,
                Unique(servers.SelectMany(server => server.Databases
                    .Where(database => database.LongestQuerySeconds is not null)
                    .Select(database => (Of(server, database.Name), (Server: server, Database: database))))),
                (pair, current) => LongQuery(pair.Server, pair.Database, current, settings.DatabaseLongQuery),
                now,
                cancellationToken);
        }

        if (settings.DatabaseBackupAge.Enabled)
        {
            await ApplyAsync(
                AlertRules.DatabaseBackupAge,
                Unique(servers.SelectMany(server => server.Databases
                    .Where(database => database.BackupAgeSeconds is not null)
                    .Where(database => DatabaseBackupAgeRule.Judges(database.Name, settings.DatabaseBackupAge))
                    .Select(database => (Of(server, database.Name), (Server: server, Database: database))))),
                (pair, current) => BackupAge(pair.Server, pair.Database, current, settings.DatabaseBackupAge),
                now,
                cancellationToken);
        }

        if (settings.DatabaseAgentJob.Enabled)
        {
            await ApplyAsync(
                AlertRules.DatabaseAgentJob,
                Unique(servers.SelectMany(server => server.Jobs
                    .Select(job => (Of(server, job.Name), (Server: server, Job: job))))),
                (pair, current) => AgentJob(pair.Server, pair.Job),
                now,
                cancellationToken);
        }
    }

    // The first figures of each subject: a server sending to two environments under one name is two subjects.
    private static Dictionary<Key, T> Unique<T>(IEnumerable<(Key Key, T Value)> pairs) =>
        pairs.GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.First().Value);

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

    private static RuleVerdict Blocking(DatabaseHealth server, AlertState? current, DatabaseBlockingOptions rule)
    {
        var least = server.BlockedLeast ?? 0;
        var next = DatabaseBlockingRule.Evaluate(least, server.BlockedMost ?? 0, current?.Severity, rule);

        return new RuleVerdict(
            AlertRules.DatabaseBlocking,
            server.Environment,
            AlertSubject.Database(server.Server.Name),
            next,
            least,
            next is { } severity ? DatabaseBlockingRule.ThresholdFor(severity, rule) : 0,
            DatabaseBlockingRule.Window);
    }

    private static RuleVerdict Deadlocks(DatabaseHealth server, AlertState? current, DatabaseDeadlocksOptions rule)
    {
        var quarter = server.DeadlocksQuarter ?? 0;
        var next = DatabaseDeadlocksRule.Evaluate(quarter, server.DeadlocksHalfHour ?? 0, current?.Severity, rule);

        return new RuleVerdict(
            AlertRules.DatabaseDeadlocks,
            server.Environment,
            AlertSubject.Database(server.Server.Name),
            next,
            quarter,
            next is { } severity ? DatabaseDeadlocksRule.ThresholdFor(severity, rule) : 0,
            DatabaseDeadlocksRule.Window);
    }

    private static RuleVerdict LongQuery(
        DatabaseHealth server,
        DatabasePartHealth database,
        AlertState? current,
        DatabaseLongQueryOptions rule)
    {
        var seconds = database.LongestQuerySeconds ?? 0;
        var next = DatabaseLongQueryRule.Evaluate(
            seconds,
            database.LongestQueryRecentMax ?? seconds,
            current?.Severity,
            rule);

        return new RuleVerdict(
            AlertRules.DatabaseLongQuery,
            server.Environment,
            AlertSubject.Database(server.Server.Name, database.Name),
            next,
            seconds,
            next is { } severity ? DatabaseLongQueryRule.ThresholdFor(severity, rule) : 0,
            DatabaseLongQueryRule.Window);
    }

    private static RuleVerdict BackupAge(
        DatabaseHealth server,
        DatabasePartHealth database,
        AlertState? current,
        DatabaseBackupAgeOptions rule)
    {
        var seconds = database.BackupAgeSeconds ?? 0;
        var next = DatabaseBackupAgeRule.Evaluate(seconds, current?.Severity, rule);

        return new RuleVerdict(
            AlertRules.DatabaseBackupAge,
            server.Environment,
            AlertSubject.Database(server.Server.Name, database.Name),
            next,
            seconds,
            next is { } severity ? DatabaseBackupAgeRule.ThresholdFor(severity, rule) : 0,
            DatabaseBackupAgeRule.Window);
    }

    private static RuleVerdict AgentJob(DatabaseHealth server, AgentJobHealth job) => new(
        AlertRules.DatabaseAgentJob,
        server.Environment,
        AlertSubject.Database(server.Server.Name, job.Name),
        DatabaseAgentJobRule.Evaluate(job.Failed),
        job.Failed ? 1 : 0,
        1,
        DatabaseAgentJobRule.Window);

    private sealed record Key(string? Environment, AlertSubject Subject);
}
