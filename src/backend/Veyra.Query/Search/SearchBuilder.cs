using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Search;
using Veyra.Core.Telemetry;
using Veyra.Query.Home;
using Veyra.Query.Live;
using Veyra.Query.Metrics;
using Veyra.Query.Search.Api;

namespace Veyra.Query.Search;

/// <summary>
/// Answers the search in the top bar (ADR 0022): services, hosts and containers, databases, root operations, and the
/// number of log records that hold the words, each group found, ranked, and counted on the server.
/// </summary>
/// <remarks>
/// <para>
/// The groups run side by side, each within its own <see cref="SearchBudget"/>. A group that runs out answers with what
/// it had found, marked partial, and the others are not held up. A group first finds its names, then reads the open
/// alerts for their health, then the figure beside each; it keeps the answer so far at every step.
/// </para>
/// <para>
/// Counts that grow with the window, the root operations and the log records, are read in parts, newest first, so one
/// that runs out still holds the most recent hours.
/// </para>
/// </remarks>
/// <param name="signals">Finds names in the stored signals, from the Storage module.</param>
/// <param name="logs">Counts records by word (ADR 0016), from the Storage module.</param>
/// <param name="alerts">The open alerts, from the Alerting module.</param>
/// <param name="health">The figures beside services, hosts, containers, and database servers.</param>
/// <param name="metrics">The sizes of databases.</param>
/// <param name="budget">The time each group gets.</param>
/// <param name="clock">The clock for the figures that are read for now.</param>
/// <param name="logger">Receives how long each group took.</param>
internal sealed partial class SearchBuilder(
    ISignalSearch signals,
    ILogSearch logs,
    IAlertStates alerts,
    IMetricHealth health,
    IMetricSearch metrics,
    SearchBudget budget,
    TimeProvider clock,
    ILogger<SearchBuilder> logger)
{
    /// <summary>The most items a group shows.</summary>
    public const int GroupSize = 5;

    // Operations read per part of the window. Counts add up across parts, so more than one group's worth is read: an
    // operation that is sixth in one hour can still be among the five over the day.
    private const int _operationCandidates = 200;

    // Database sizes are reported every few minutes; the newest within this long is shown.
    private static readonly TimeSpan _sizeWindow = TimeSpan.FromMinutes(15);

    private static readonly string[] _sizeMetrics = [SqlServerNames.DatabaseSize, MetricNames.PostgresDatabaseSize];

    /// <summary>Searches every group.</summary>
    /// <param name="text">The text to look for; an empty one answers nothing.</param>
    /// <param name="environment">Only this environment, or <see langword="null"/> for all.</param>
    /// <param name="from">The start of the window, inclusive.</param>
    /// <param name="to">The end of the window, exclusive.</param>
    /// <param name="cancellationToken">Cancels the search when the client goes away.</param>
    /// <returns>The groups.</returns>
    public async Task<SearchResponse> SearchAsync(
        string text,
        string? environment,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        var query = text.Trim();

        if (query.Length == 0)
        {
            return SearchResponse.Empty(query);
        }

        var now = clock.GetUtcNow();
        var search = new NameSearch(query, from, to, environment, GroupSize);
        var window = WindowWords(to - from);

        // One read of the open alerts serves the three groups that show health; each waits for it within its own time.
        var open = alerts.ListAsync(now, cancellationToken);
        Observe(open);

        var services = RunAsync<IReadOnlyList<SearchItemResponse>>(
            "services",
            [],
            (progress, token) => ServicesAsync(search, open, now, progress, token),
            cancellationToken);
        var hosts = RunAsync<IReadOnlyList<SearchItemResponse>>(
            "hosts",
            [],
            (progress, token) => HostsAsync(search, open, now, progress, token),
            cancellationToken);
        var databases = RunAsync<IReadOnlyList<SearchItemResponse>>(
            "databases",
            [],
            (progress, token) => DatabasesAsync(search, open, now, progress, token),
            cancellationToken);
        var traces = RunAsync<IReadOnlyList<SearchOperationResponse>>(
            "traces",
            [],
            (progress, token) => TracesAsync(search, window, progress, token),
            cancellationToken);
        var count = RunAsync(
            "logs",
            0L,
            (progress, token) => CountLogsAsync(search, progress, token),
            cancellationToken);

        await Task.WhenAll(services, hosts, databases, traces, count);

        return new SearchResponse(
            query,
            Group(await services),
            Group(await hosts),
            Group(await databases),
            Group(await traces),
            new SearchCountResponse((await count).Value, (await count).Partial));
    }

    /// <summary>
    /// Runs one group within its budget and returns its answer, or what it had when the time ran out.
    /// </summary>
    private async Task<(T Value, bool Partial)> RunAsync<T>(
        string group,
        T initial,
        Func<SearchProgress<T>, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        var progress = new SearchProgress<T>(initial);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var started = Stopwatch.GetTimestamp();
        var running = work(progress, stop.Token);
        var partial = false;

        try
        {
            // The budget is time as people wait it, whatever clock decides what now is.
            await running.WaitAsync(budget.PerGroup, TimeProvider.System, cancellationToken);
        }
        catch (TimeoutException)
        {
            partial = true;

            // The group's reads are cancelled, but nobody waits for them to stop.
            await stop.CancelAsync();
            Observe(running);
        }

        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogGroup(group, milliseconds, partial);

        return (progress.Value, partial);
    }

    private async Task ServicesAsync(
        NameSearch search,
        Task<IReadOnlyList<AlertState>> open,
        DateTimeOffset now,
        SearchProgress<IReadOnlyList<SearchItemResponse>> progress,
        CancellationToken cancellationToken)
    {
        var found = Best(await signals.FindBlocksAsync(search, [AlertBlockKind.Service], cancellationToken), search);

        progress.Value = found.Select(block => Item(block, null, Logs(block))).ToList();

        if (found.Count == 0)
        {
            return;
        }

        var alertsNow = await open.WaitAsync(cancellationToken);

        progress.Value = found
            .Select(block => Item(block, HealthOf(block, search.Environment, alertsNow), Logs(block)))
            .ToList();

        var latencies = await health.ListLatenciesAsync(now, cancellationToken);

        progress.Value = found
            .Select(block =>
            {
                // The slowest environment, when the service runs in several and none was chosen.
                var p95 = latencies
                    .Where(latency => latency.Service == block.Name
                        && (search.Environment is null || latency.Environment == search.Environment))
                    .Max(latency => latency.P95Seconds);

                return Item(
                    block,
                    HealthOf(block, search.Environment, alertsNow),
                    p95 is { } seconds ? Latency(seconds) : Logs(block));
            })
            .ToList();
    }

    private async Task HostsAsync(
        NameSearch search,
        Task<IReadOnlyList<AlertState>> open,
        DateTimeOffset now,
        SearchProgress<IReadOnlyList<SearchItemResponse>> progress,
        CancellationToken cancellationToken)
    {
        var found = Best(
            await signals.FindBlocksAsync(search, [AlertBlockKind.Host, AlertBlockKind.Container], cancellationToken),
            search);

        progress.Value = found.Select(block => Item(block, null, Logs(block))).ToList();

        if (found.Count == 0)
        {
            return;
        }

        var alertsNow = await open.WaitAsync(cancellationToken);

        progress.Value = found
            .Select(block => Item(block, HealthOf(block, search.Environment, alertsNow), Logs(block)))
            .ToList();

        var disks = found.Any(block => block.Kind == AlertBlockKind.Host)
            ? await health.ListDisksAsync(now, cancellationToken)
            : [];
        var containers = found.Any(block => block.Kind == AlertBlockKind.Container)
            ? await health.ListContainersAsync(now, cancellationToken)
            : [];

        progress.Value = found
            .Select(block =>
            {
                // The fullest disk of a host; the fullest memory of a container that runs on several hosts.
                var ratio = block.Kind == AlertBlockKind.Host
                    ? disks.Where(disk => disk.Host == block.Name).Select(disk => (double?)disk.Ratio).Max()
                    : containers
                        .Where(container => container.Container == block.Name)
                        .Max(container => container.MemoryRatio);
                var detail = ratio is { } share
                    ? $"{(block.Kind == AlertBlockKind.Host ? "disk" : "memory")} {Percent(share)}"
                    : Logs(block);

                return Item(block, HealthOf(block, search.Environment, alertsNow), detail);
            })
            .ToList();
    }

    private async Task DatabasesAsync(
        NameSearch search,
        Task<IReadOnlyList<AlertState>> open,
        DateTimeOffset now,
        SearchProgress<IReadOnlyList<SearchItemResponse>> progress,
        CancellationToken cancellationToken)
    {
        var found = Best(await signals.FindBlocksAsync(search, [AlertBlockKind.Database], cancellationToken), search);

        progress.Value = found.Select(block => Item(block, null, null)).ToList();

        if (found.Count == 0)
        {
            return;
        }

        var alertsNow = await open.WaitAsync(cancellationToken);

        progress.Value = found
            .Select(block => Item(block, HealthOf(block, search.Environment, alertsNow), null))
            .ToList();

        var servers = found.Any(block => block.Database is null)
            ? await health.ListDatabasesAsync(now, cancellationToken)
            : [];
        var sizes = await SizesAsync(found.Where(block => block.Database is not null), now, cancellationToken);

        progress.Value = found
            .Select(block =>
            {
                string? detail;

                if (block.Database is { } database)
                {
                    detail = sizes.TryGetValue((block.Name, database), out var bytes) ? Bytes(bytes) : null;
                }
                else
                {
                    var server = servers.FirstOrDefault(one => one.Server.Name == block.Name);
                    detail = server is null ? null : Words.Count(server.Databases.Count, "database");
                }

                return Item(block, HealthOf(block, search.Environment, alertsNow), detail);
            })
            .ToList();
    }

    // The newest size of each database, from the size metric of its server; a database split into files counts all.
    private async Task<Dictionary<(string Server, string Database), double>> SizesAsync(
        IEnumerable<FoundBlock> databases,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sizes = new Dictionary<(string Server, string Database), double>();

        foreach (var server in databases.Select(block => block.Name).Distinct(StringComparer.Ordinal))
        {
            var series = await metrics.FindSeriesAsync(
                new MetricSeriesQuery(
                    now - _sizeWindow,
                    _sizeMetrics,
                    Block: new TelemetryBlock(AlertBlockKind.Database, server)),
                cancellationToken);

            if (series.Count == 0)
            {
                continue;
            }

            var buckets = await metrics.ReadBucketsAsync(
                series.Select(one => one.Id).ToList(),
                now - _sizeWindow,
                now,
                _sizeWindow,
                cancellationToken);

            foreach (var one in series)
            {
                var name = one.Attributes.GetValueOrDefault(SqlServerNames.DatabaseAttribute)
                    ?? one.Attributes.GetValueOrDefault(MetricNames.PostgresDatabase);
                var last = buckets.LastOrDefault(bucket => bucket.SeriesId == one.Id)?.LastValue;

                if (name is not null && last is { } value)
                {
                    sizes[(server, name)] = sizes.GetValueOrDefault((server, name)) + value;
                }
            }
        }

        return sizes;
    }

    private async Task TracesAsync(
        NameSearch search,
        string window,
        SearchProgress<IReadOnlyList<SearchOperationResponse>> progress,
        CancellationToken cancellationToken)
    {
        var totals = new Dictionary<(string Name, string Service), (long Count, long Failed)>();

        foreach (var (start, end) in SearchChunks.NewestFirst(search.From, search.To))
        {
            var part = await signals.FindOperationsAsync(
                search with { From = start, To = end, Limit = _operationCandidates },
                cancellationToken);

            foreach (var operation in part)
            {
                var (count, failed) = totals.GetValueOrDefault((operation.Name, operation.Service));
                totals[(operation.Name, operation.Service)] = (count + operation.Count, failed + operation.Failed);
            }

            progress.Value = SearchRanking
                .Rank(totals, entry => entry.Key.Name, search.Text)
                .Take(GroupSize)
                .Select(entry => new SearchOperationResponse(
                    "operation",
                    entry.Key.Name,
                    entry.Key.Service,
                    entry.Value.Count,
                    OperationWords(entry.Value.Count, entry.Value.Failed, window)))
                .ToList();
        }
    }

    private async Task CountLogsAsync(
        NameSearch search,
        SearchProgress<long> progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? environments = search.Environment is null ? null : [search.Environment];
        long total = 0;

        foreach (var (start, end) in SearchChunks.NewestFirst(search.From, search.To))
        {
            // The limit is required by the query record but not used for counting.
            var summary = await logs.SummarizeAsync(
                new LogQuery(start, end, null, null, search.Text, null, 1, null, environments),
                end - start,
                cancellationToken);

            total += summary.Buckets.Sum(bucket => bucket.Total);
            progress.Value = total;
        }
    }

    // A read that nobody awaits any more, because its group ran out or found nothing, must not fail unobserved.
    private static void Observe(Task task) => _ = task.ContinueWith(
        finished => _ = finished.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted,
        TaskScheduler.Default);

    private static List<FoundBlock> Best(IReadOnlyList<FoundBlock> found, NameSearch search) =>
        SearchRanking.Rank(found.DistinctBy(block => (block.Kind, block.Name, block.Database)), Shown, search.Text)
            .Take(GroupSize)
            .ToList();

    // The name a block is found and shown by: a database server by its server, a database by its own name.
    private static string Shown(FoundBlock block) =>
        block.Database ?? (block.Kind == AlertBlockKind.Database ? ServerOf(block.Name) : block.Name);

    private static string ServerOf(string name) => new TelemetryBlock(AlertBlockKind.Database, name).Server ?? name;

    private static SearchItemResponse Item(FoundBlock block, BlockHealth? state, string? detail)
    {
        var kind = block.Kind switch
        {
            AlertBlockKind.Database => block.Database is null ? "server" : "database",
            _ => AlertSubject.KindNameOf(block.Kind),
        };

        if (block.Kind != AlertBlockKind.Database)
        {
            return new SearchItemResponse(kind, block.Name, state, detail);
        }

        var server = new TelemetryBlock(AlertBlockKind.Database, block.Name);

        return new SearchItemResponse(kind, Shown(block), state, detail)
        {
            Engine = server.Engine,
            Server = server.Server,
        };
    }

    /// <summary>
    /// A block's state from its open alerts, any part of it included; a database only from the alerts about it.
    /// Blocks that name no environment belong to every one, as on the Metrics screens.
    /// </summary>
    private static BlockHealth HealthOf(FoundBlock block, string? environment, IReadOnlyList<AlertState> open)
    {
        var worst = open
            .Where(alert => alert.ResolvedAt is null
                && alert.Subject.Kind == block.Kind
                && alert.Subject.Name == block.Name
                && (block.Database is null || alert.Subject.Part == block.Database)
                && (environment is null
                    || alert.Environment == environment
                    || (block.Kind != AlertBlockKind.Service && alert.Environment is null)))
            .Select(alert => (AlertSeverity?)alert.Severity)
            .Max();

        return worst switch
        {
            AlertSeverity.Warning => BlockHealth.Warning,
            AlertSeverity.Caution => BlockHealth.Caution,
            _ => BlockHealth.Ok,
        };
    }

    private static SearchGroupResponse<T> Group<T>((IReadOnlyList<T> Value, bool Partial) result) =>
        new(result.Value, result.Partial);

    private static string? Logs(FoundBlock block) => block.Records > 0 ? Words.Count(block.Records, "log") : null;

    private static string Latency(double seconds) => seconds < 1
        ? string.Create(CultureInfo.InvariantCulture, $"{seconds * 1000:0} ms p95")
        : string.Create(CultureInfo.InvariantCulture, $"{seconds:0.0} s p95");

    private static string Percent(double ratio) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(ratio * 100):0}%");

    private static string Bytes(double bytes) => bytes switch
    {
        >= 1L << 40 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1L << 40):0.#} TB"),
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1L << 30):0.#} GB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1L << 20):0} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024:0} KB"),
    };

    private static string OperationWords(long count, long failed, string window)
    {
        var words = string.Create(CultureInfo.InvariantCulture, $"{count:N0} in {window}");

        return failed > 0 ? string.Create(CultureInfo.InvariantCulture, $"{words}, {failed:N0} failed") : words;
    }

    // The window as the top bar's presets name it: 15 min, 24 h, 7 d.
    private static string WindowWords(TimeSpan window) => window switch
    {
        { TotalHours: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(window.TotalMinutes):0} min"),
        { TotalDays: < 2 } => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(window.TotalHours):0} h"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(window.TotalDays):0} d"),
    };

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Search group {Group} took {Milliseconds} ms; partial: {Partial}")]
    private partial void LogGroup(string group, double milliseconds, bool partial);
}
