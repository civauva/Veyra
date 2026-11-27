using System.Collections.Concurrent;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Metrics.Health;
using Veyra.Query.Live;

namespace Veyra.Query.Metrics;

/// <summary>
/// The figures the Alerting module judges metrics by (ADR 0012), computed with the same views that draw the Metrics
/// screens and the home, so an alert and the screen it links to always agree.
/// </summary>
/// <param name="search">The stored metrics.</param>
/// <param name="activity">
/// The reads of database servers' live views (ADR 0020), or <see langword="null"/> where only metrics are judged.
/// </param>
internal sealed class MetricHealth(IMetricSearch search, IDatabaseActivitySearch? activity = null) : IMetricHealth
{
    // Only senders that are still sending count, as on the overview.
    private static readonly TimeSpan _activeWithin = TimeSpan.FromMinutes(15);

    // The deadlock rule counts over 15 minutes and clears after 30 without one (ADR 0018).
    private static readonly TimeSpan _deadlocksWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan _deadlocksClearAfter = TimeSpan.FromMinutes(30);

    // The restart rule counts over 15 minutes and clears after as long without one (ADR 0018).
    private static readonly TimeSpan _restartsWindow = TimeSpan.FromMinutes(15);

    // Six series per container: room for well over a thousand containers.
    private const int _maxContainerSeries = 10_000;

    // What the database rules judge, of SQL Server and of PostgreSQL.
    private static readonly string[] _databaseRules =
    [
        SqlServerNames.Blocked,
        SqlServerNames.DeadlockRate,
        SqlServerNames.LongestQuery,
        SqlServerNames.BackupAge,
        SqlServerNames.JobFailed,
        .. MetricNames.PostgresRules,
    ];

    // The deadlocks of both engines, counted over the last 30 minutes.
    private static readonly string[] _deadlocks = [SqlServerNames.DeadlockRate, MetricNames.PostgresDeadlocks];

    // An alert's chart has no more points than this.
    private const int _maxPoints = 240;

    private readonly MetricReader _reader = new(search);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiskHealth>> ListDisksAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: [MetricNames.Filesystem]),
                cancellationToken))
            .Where(one => one.HostName is not null && one.ServiceName is null)
            .ToList();

        if (series.Count == 0)
        {
            return [];
        }

        var recent = await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken);
        var day = await _reader.ReadAsync(series, MetricWindow.Day(now), cancellationToken);

        return series
            .GroupBy(one => one.HostName!, StringComparer.Ordinal)
            .SelectMany(host =>
            {
                var environment = host.Select(one => one.Environment).FirstOrDefault(value => value is not null);
                var ofHost = recent.Where(one => one.HostName == host.Key);

                return HostView.Detail(host.Key, ofHost, ofHost, day.Where(one => one.HostName == host.Key))
                    .Disks
                    .Select(disk => new DiskHealth(
                        host.Key,
                        environment,
                        disk.Mountpoint,
                        disk.UsedBytes,
                        disk.TotalBytes,
                        disk.Ratio,
                        disk.DaysUntilFull));
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContainerHealth>> ListContainersAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: MetricNames.Container, Limit: _maxContainerSeries),
                cancellationToken))
            .Where(ContainerView.IsContainer)
            .ToList();

        if (series.Count == 0)
        {
            return [];
        }

        // Both reads come from the minute rollups: five buckets of every series, and 16 of the restart counts.
        var recent = await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken);
        var quarter = await _reader.ReadAsync(
            series.Where(one => one.Name == MetricNames.ContainerRestarts).ToList(),
            MetricWindow.Covering(now - _restartsWindow, now, 15),
            cancellationToken);

        return ContainerView.Health(recent, quarter);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DatabaseMeasure>> ListDatabaseMeasuresAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reads = activity is null
            ? []
            : await activity.ListLatestSessionsAsync(now, LiveViewBuilder.MaxAge, cancellationToken);
        var measures = reads.SelectMany(LiveMeasures.Of).ToList();

        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: MetricMeasures.Names, Limit: 20_000),
                cancellationToken))
            .Where(one => one.Block?.Kind == AlertBlockKind.Database)
            .ToList();

        if (series.Count > 0)
        {
            var recent = await _reader.ReadAsync(
                series,
                MetricWindow.Covering(now.AddMinutes(-15), now, 15),
                cancellationToken);

            foreach (var server in series.GroupBy(one => one.Block!))
            {
                measures.AddRange(MetricMeasures.Of(
                    recent.Where(one => one.Block == server.Key),
                    server.Key,
                    server.Select(one => one.Environment).FirstOrDefault(value => value is not null)));
            }
        }

        foreach (var read in reads)
        {
            var events = (await activity!.ListRecordsAsync(
                    read.Block,
                    Server.EventsViewBuilder.EventKind,
                    now.AddMinutes(-15),
                    now,
                    null,
                    1_000,
                    cancellationToken))
                .GroupBy(record => record.Key)
                .Select(group => Server.EventsViewBuilder.Event(group.First()))
                .ToList();
            var server = new Core.Telemetry.TelemetryBlock(AlertBlockKind.Database, read.Block);

            measures.Add(new DatabaseMeasure(
                server,
                read.Environment,
                "severe-errors",
                null,
                events.Count(one => Server.EventsViewBuilder.Counts("severe-errors", one))));
            measures.Add(new DatabaseMeasure(
                server,
                read.Environment,
                "memory-errors",
                null,
                events.Count(one => Server.EventsViewBuilder.Counts("memory-errors", one))));
            measures.Add(new DatabaseMeasure(
                server,
                read.Environment,
                "login-failures",
                null,
                events.Count(one => Server.EventsViewBuilder.Counts("login-failures", one))));

            measures.Add(new DatabaseMeasure(
                new Core.Telemetry.TelemetryBlock(AlertBlockKind.Database, read.Block),
                read.Environment,
                RegressionMeasure,
                null,
                await RegressionAsync(read.Block, now, cancellationToken)));
        }

        return measures;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerError>> ListServerErrorsAsync(
        string server,
        string measure,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        if (activity is null)
        {
            return [];
        }

        // The same events, and the same test, as the measure the rule judged.
        var records = await activity.ListRecordsAsync(
            server,
            Server.EventsViewBuilder.EventKind,
            periodStart,
            periodEnd,
            null,
            1_000,
            cancellationToken);

        return records
            .GroupBy(record => record.Key)
            .Select(group => Server.EventsViewBuilder.Event(group.First()))
            .Where(one => Server.EventsViewBuilder.Counts(measure, one))
            .OrderByDescending(one => one.At)
            .Select(one => new ServerError(
                one.At,
                one.Error,
                one.ErrorSeverity,
                string.IsNullOrWhiteSpace(one.Message) ? one.Words : one.Message))
            .ToList();
    }

    /// <summary>The measure of the plan-regression rule (ADR 0020).</summary>
    public const string RegressionMeasure = "plan-regression-factor";

    // Reading every statement series is too much for every evaluation; a server's factor is kept for five minutes. Two
    // evaluations at once may both compute it, which is harmless.
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, double Factor)> _regressions =
        new(StringComparer.Ordinal);

    private async Task<double> RegressionAsync(string block, DateTimeOffset now, CancellationToken cancellationToken)
    {
        {
            if (_regressions.TryGetValue(block, out var kept) && now - kept.At < TimeSpan.FromMinutes(5))
            {
                return kept.Factor;
            }

            var server = new Core.Telemetry.TelemetryBlock(AlertBlockKind.Database, block);
            var series = await search.FindSeriesAsync(
                new MetricSeriesQuery(now.AddHours(-1), Names: SqlServerNames.Statements, Limit: 8_000, Block: server),
                cancellationToken);
            var totals = series.Count == 0
                ? []
                : (await search.ReadBucketsAsync(
                        series.Select(one => one.Id).ToList(),
                        now.AddHours(-1),
                        now,
                        TimeSpan.FromHours(1),
                        cancellationToken))
                    .GroupBy(bucket => bucket.SeriesId)
                    .ToDictionary(group => group.Key, group => group.Sum(bucket => bucket.Total ?? 0));
            var factor = Queries.QueriesViewBuilder.Statements(series, totals)
                .Where(statement => !statement.Agent)
                .Select(statement => Queries.QueriesViewBuilder.RegressionFactor(statement))
                .DefaultIfEmpty(0)
                .Max();

            _regressions[block] = (now, factor);

            return factor;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DatabaseHealth>> ListDatabasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: _databaseRules),
                cancellationToken))
            .Where(one => one.Block?.Kind == AlertBlockKind.Database)
            .ToList();

        if (series.Count == 0)
        {
            return [];
        }

        var recent = await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken);
        var halfHour = await _reader.ReadAsync(
            series.Where(one => _deadlocks.Contains(one.Name)).ToList(),
            MetricWindow.Covering(now - _deadlocksClearAfter, now, 30),
            cancellationToken);

        return series
            .GroupBy(one => one.Block!)
            .Select(server =>
            {
                var ofServer = recent.Where(one => one.Block == server.Key);
                var deadlocks = DatabaseView.Deadlocks(halfHour.Where(one => one.Block == server.Key));
                var blocked = ofServer.Levels(ofServer.Named(SqlServerNames.Blocked, MetricNames.PostgresBlocked));

                // Blocked throughout means in every minute of the five: a server that started sending a minute ago
                // has not shown that yet.
                var throughout = ofServer.Window.Starts.All(blocked.ContainsKey);
                var minutes = blocked
                    .Where(pair => pair.Key >= ofServer.Window.Start)
                    .Select(pair => pair.Value)
                    .ToList();
                var hasDeadlocks = ofServer.Named(_deadlocks).Any();
                var longest = DatabaseView.LatestBy(
                        ofServer,
                        SqlServerNames.LongestQuery,
                        SqlServerNames.DatabaseAttribute)
                    .Concat(PostgresView.ByDatabase(ofServer, MetricNames.PostgresLongestQuery))
                    .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value), StringComparer.Ordinal);
                var backups = DatabaseView.LatestBy(
                    ofServer,
                    SqlServerNames.BackupAge,
                    SqlServerNames.DatabaseAttribute);
                var ages = PostgresView.ByDatabase(ofServer, MetricNames.PostgresTransactionIdAge);
                var jobs = DatabaseView.LatestBy(ofServer, SqlServerNames.JobFailed, SqlServerNames.JobAttribute);

                return new DatabaseHealth(
                    server.Key,
                    server.Select(one => one.Environment).FirstOrDefault(value => value is not null),
                    minutes.Count == 0 ? null : throughout ? minutes.Min() : 0,
                    minutes.Count == 0 ? null : minutes.Max(),
                    hasDeadlocks
                        ? Math.Round(deadlocks
                            .Where(pair => pair.Key >= now - _deadlocksWindow)
                            .Sum(pair => pair.Value))
                        : null,
                    hasDeadlocks ? Math.Round(deadlocks.Values.Sum()) : null,
                    longest.Keys
                        .Union(backups.Keys, StringComparer.Ordinal)
                        .Union(ages.Keys, StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .Select(database => new DatabasePartHealth(
                            database,
                            longest.TryGetValue(database, out var seconds) ? seconds : null,
                            RecentMax(ofServer, database),
                            backups.TryGetValue(database, out var age) ? age : null)
                        {
                            WraparoundRatio = ages.TryGetValue(database, out var transactions)
                                ? transactions / MetricNames.PostgresWraparoundLimit
                                : null,
                        })
                        .ToList(),
                    jobs.OrderBy(job => job.Key, StringComparer.Ordinal)
                        .Select(job => new AgentJobHealth(job.Key, job.Value >= 1))
                        .ToList())
                {
                    ConnectionsRatio = PostgresView.ConnectionsRatio(ofServer),
                };
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceLatency>> ListLatenciesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: [MetricNames.RequestDuration]),
                cancellationToken))
            .Where(one => one.ServiceName is not null)
            .ToList();
        var data = await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken);

        return series
            .GroupBy(one => (Service: one.ServiceName!, one.Environment))
            .Select(group =>
            {
                var total = data.Total(data.Increases(group));

                return new ServiceLatency(
                    group.Key.Service,
                    group.Key.Environment,
                    total?.Quantile(0.95),
                    total?.Count ?? 0);
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<DiskHistory> ReadDiskAsync(
        string host,
        string mountpoint,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Covering(periodStart, periodEnd, _maxPoints);
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(window.ReadStart, Names: [MetricNames.Filesystem], HostName: host),
                cancellationToken))
            .Where(one => one.ServiceName is null)
            .ToList();
        var data = await _reader.ReadAsync(series, window, cancellationToken);
        var share = HostView.DiskShare(data, mountpoint);

        return new DiskHistory(
            window.Bucket,
            window.Starts
                .Select(start => new DiskUsagePoint(start, share.TryGetValue(start, out var ratio) ? ratio : null))
                .ToList());
    }

    /// <inheritdoc />
    public async Task<LatencyHistory> ReadLatencyAsync(
        string service,
        string? environment,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Covering(periodStart, periodEnd, _maxPoints);
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(
                window.ReadStart,
                Names: [MetricNames.RequestDuration],
                ServiceName: service,
                Environment: environment),
            cancellationToken);
        var data = await _reader.ReadAsync(series, window, cancellationToken);
        var increases = data.Increases(data.Series);

        var points = window.Starts
            .Select(start => increases.TryGetValue(start, out var bucket)
                ? new LatencyPoint(start, bucket.Quantile(0.5), bucket.Quantile(0.95), bucket.Count)
                : new LatencyPoint(start, null, null, 0))
            .ToList();
        var routes = ServiceView.Routes(data)
            .OrderByDescending(route => route.P95Seconds ?? 0)
            .Select(route => new RouteLatency(
                route.Method,
                route.Route,
                route.RequestsPerSecond,
                route.P95Seconds,
                route.ServerErrorRatio))
            .ToList();

        return new LatencyHistory(window.Bucket, points, routes);
    }

    private static double? RecentMax(SeriesData data, string database)
    {
        var levels = data.Levels(data.Named(SqlServerNames.LongestQuery)
            .Where(one => MetricNames.Attribute(one, SqlServerNames.DatabaseAttribute) == database)
            .Concat(data.Named(MetricNames.PostgresLongestQuery)
                .Where(one => MetricNames.PostgresDatabaseOf(one) == database)));

        return levels.Count > 0 ? levels.Values.Max() : null;
    }
}
