using System.Collections.Concurrent;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;

namespace Veyra.Query.Home;

/// <summary>
/// Builds the home snapshot from the stored logs, the open alerts, and the storage measurement.
/// </summary>
/// <remarks>
/// <para>
/// Health comes only from alerts (ADR 0007): a service with many errors but no open alert is not lit. The numbers
/// beside it are shown, but the colour waits for the rule.
/// </para>
/// <para>
/// The seven-day history behind the expected load is read once per hour and environment and kept in memory, because
/// it changes only when the hour does and it is the most expensive part of the snapshot.
/// </para>
/// </remarks>
/// <param name="rates">Record and error counts per service, from the Storage module.</param>
/// <param name="search">Hourly record counts, from the Storage module.</param>
/// <param name="alerts">Open and recently resolved alerts, from the Alerting module.</param>
/// <param name="storage">The size of log storage, from the Storage module.</param>
/// <param name="metrics">Request durations and disk usage, from the Storage module.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class HomeSnapshotBuilder(
    ILogRates rates,
    ILogSearch search,
    IAlertStates alerts,
    ILogStorageUsage storage,
    IMetricSearch metrics,
    TimeProvider clock)
{
    /// <summary>The group every service is in until groupings exist.</summary>
    public const string ServicesGroup = "Services";

    /// <summary>The group every host is in until groupings exist.</summary>
    public const string HostsGroup = "Hosts";

    // The same window the built-in error-rate rule uses by default, so the numbers match the alerts.
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(5);

    // A service that logged within the last hour is still shown, even when it is quiet right now.
    private static readonly TimeSpan _blockWindow = TimeSpan.FromHours(1);

    private static readonly TimeSpan _resolvedWindow = TimeSpan.FromDays(1);
    private static readonly TimeSpan _hourLength = TimeSpan.FromHours(1);

    // A full day, this hour included: the strip shows the last seven, the large-screen chart all of them (VEY-24).
    private const int _observedHours = 23;
    private const int _expectedHours = 6;
    private const int _historyDays = 7;

    private readonly ConcurrentDictionary<(DateTimeOffset Hour, string? Environment), ExpectedLoad> _expected = new();

    private readonly HomeMetricsOutlook _metrics = new(metrics);

    /// <summary>Builds the snapshot of one environment, or of all of them together.</summary>
    /// <param name="environment">The environment to show, or null for all.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The snapshot.</returns>
    public async Task<HomeResponse> BuildAsync(string? environment, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var hour = new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerHour), TimeSpan.Zero);
        var scope = new Scope(environment);

        var current = await rates.GetServiceRatesAsync(now - _window, now, cancellationToken);
        var recent = await rates.GetServiceRatesAsync(now - _blockWindow, now, cancellationToken);
        var known = await alerts.ListAsync(now - _resolvedWindow, cancellationToken);
        var usage = await storage.GetUsageAsync(cancellationToken);
        var observed = await search.SummarizeAsync(
            Query(hour.AddHours(-_observedHours), now, environment),
            _hourLength,
            cancellationToken);
        var expected = await ExpectedAsync(hour, environment, cancellationToken);
        var latency = await _metrics.LatencyAsync(environment, now, cancellationToken);
        var disks = await _metrics.DisksAsync(environment, now, cancellationToken);

        var open = known.Where(alert => alert.ResolvedAt is null).ToList();
        var numbered = open
            .Where(alert => scope.Includes(alert.Environment))
            .OrderByDescending(alert => alert.Severity)
            .ThenBy(alert => alert.OpenedAt)
            .ThenBy(alert => alert.Subject.Name, StringComparer.Ordinal)
            .ThenBy(alert => alert.Subject.Part, StringComparer.Ordinal)
            .Select((alert, index) => ToResponse(alert, index + 1))
            .ToList();
        var resolved = known
            .Where(alert => alert.ResolvedAt is not null && scope.Includes(alert.Environment))
            .OrderByDescending(alert => alert.ResolvedAt)
            .Select(alert => ToResponse(alert, null))
            .ToList();

        var scoped = current.Where(rate => scope.Includes(rate.Environment)).ToList();
        var records = scoped.Sum(rate => rate.Total);
        var ratio = Ratio(records, scoped.Sum(rate => rate.Errors));
        var kind = ConditionOf(numbered.Select(alert => alert.Severity));
        var blocks = Blocks(scope, current, recent, open);

        return new HomeResponse(
            environment,
            now,
            Environments(current, recent, open),
            new HomeConditionResponse(kind, kind == HomeConditionKind.Clear ? null : GroupOf(numbered[0]), ratio),
            new HealthCountsResponse(
                blocks.Count(block => block.Health == BlockHealth.Warning),
                blocks.Count(block => block.Health == BlockHealth.Caution),
                blocks.Count(block => block.Health == BlockHealth.Ok),
                blocks.Count(block => block.Health == BlockHealth.Unknown)),
            numbered,
            resolved,
            blocks,
            new HomeMetricsResponse(ratio, records / _window.TotalSeconds, records, (int)_window.TotalSeconds, latency),
            new HourlyResponse(observed.Buckets, expected.Hours, expected.DaysOfHistory),
            LogStorageOutlook.From(usage),
            disks);
    }

    private async Task<ExpectedLoad> ExpectedAsync(
        DateTimeOffset hour,
        string? environment,
        CancellationToken cancellationToken)
    {
        if (_expected.TryGetValue((hour, environment), out var cached))
        {
            return cached;
        }

        var history = await search.SummarizeAsync(
            Query(hour.AddDays(-_historyDays), hour, environment),
            _hourLength,
            cancellationToken);
        var expected = ExpectedLoad.Calculate(history.Buckets, hour, _expectedHours);

        // Entries of earlier hours are never read again.
        foreach (var key in _expected.Keys.Where(key => key.Hour != hour))
        {
            _expected.TryRemove(key, out _);
        }

        _expected[(hour, environment)] = expected;

        return expected;
    }

    private static List<BlockResponse> Blocks(
        Scope scope,
        IReadOnlyList<ServiceLogRate> current,
        IReadOnlyList<ServiceLogRate> recent,
        List<AlertState> open)
    {
        var now = current.ToDictionary(rate => (rate.Environment, rate.ServiceName));

        // Services are seen in their logs; hosts only through their alerts, until hosts have a block of their own.
        var keys = current.Select(rate => (rate.Environment, Kind: AlertBlockKind.Service, Name: rate.ServiceName))
            .Concat(recent.Select(rate => (rate.Environment, Kind: AlertBlockKind.Service, Name: rate.ServiceName)))
            .Concat(open.Select(alert => (alert.Environment, alert.Subject.Kind, alert.Subject.Name)))
            .Where(key => scope.Includes(key.Environment))
            .Distinct()
            .OrderBy(key => key.Kind)
            .ThenBy(key => key.Name, StringComparer.Ordinal)
            .ThenBy(key => key.Environment, StringComparer.Ordinal);

        return keys
            .Select(key =>
            {
                var subject = new AlertSubject(key.Kind, key.Name);
                var rate = key.Kind == AlertBlockKind.Service
                    ? now.GetValueOrDefault((key.Environment, key.Name))
                    : null;
                var lit = open
                    .Where(alert => alert.Environment == key.Environment
                        && alert.Subject.Kind == key.Kind
                        && alert.Subject.Name == key.Name)
                    .ToList();
                var health = lit.Count == 0
                    ? BlockHealth.Ok
                    : lit.Max(alert => alert.Severity) == AlertSeverity.Warning
                        ? BlockHealth.Warning
                        : BlockHealth.Caution;

                return new BlockResponse(
                    subject.BlockId(key.Environment),
                    subject.KindName,
                    key.Name,
                    key.Environment,
                    health,
                    lit.Select(alert => alert.Id).ToList(),
                    rate?.Total ?? 0,
                    rate?.Errors ?? 0,
                    Ratio(rate?.Total ?? 0, rate?.Errors ?? 0));
            })
            .ToList();
    }

    private static List<HomeEnvironmentResponse> Environments(
        IReadOnlyList<ServiceLogRate> current,
        IReadOnlyList<ServiceLogRate> recent,
        List<AlertState> open)
    {
        var names = current.Select(rate => rate.Environment)
            .Concat(recent.Select(rate => rate.Environment))
            .Concat(open.Select(alert => alert.Environment))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        return names
            .Select(name =>
            {
                var rates = current.Where(rate => rate.Environment == name).ToList();
                var severities = open.Where(alert => alert.Environment == name).Select(alert => alert.Severity);

                return new HomeEnvironmentResponse(
                    name,
                    ConditionOf(severities),
                    Ratio(rates.Sum(rate => rate.Total), rates.Sum(rate => rate.Errors)));
            })
            .ToList();
    }

    private static HomeConditionKind ConditionOf(IEnumerable<AlertSeverity> severities)
    {
        var worst = severities.Select(severity => (AlertSeverity?)severity).Max();

        return worst switch
        {
            AlertSeverity.Warning => HomeConditionKind.Storm,
            AlertSeverity.Caution => HomeConditionKind.Showers,
            _ => HomeConditionKind.Clear,
        };
    }

    private static string GroupOf(HomeAlertResponse alert) => alert.Kind == "host" ? HostsGroup : ServicesGroup;

    private static HomeAlertResponse ToResponse(AlertState alert, int? number) => new(
        number,
        alert.Id,
        alert.Subject.BlockId(alert.Environment),
        alert.Subject.Kind == AlertBlockKind.Service ? alert.Subject.Name : null,
        alert.Subject.KindName,
        alert.Subject.Name,
        alert.Subject.Part,
        alert.Environment,
        alert.Rule,
        alert.Severity,
        alert.Value,
        alert.Peak,
        alert.Threshold,
        (int)alert.Window.TotalSeconds,
        alert.OpenedAt,
        alert.ResolvedAt);

    private static double? Ratio(long total, long errors) => total == 0 ? null : (double)errors / total;

    // The limit is required by the query record but not used for counting.
    private static LogQuery Query(DateTimeOffset from, DateTimeOffset to, string? environment) =>
        new(from, to, null, null, null, null, 1, null, environment is null ? null : [environment]);

    // Null means every environment, including records without one.
    private sealed record Scope(string? Environment)
    {
        public bool Includes(string? environment) => Environment is null || environment == Environment;
    }
}
