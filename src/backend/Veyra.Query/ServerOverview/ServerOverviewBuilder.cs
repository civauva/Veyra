using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Live;
using Veyra.Query.Live.Api;
using Veyra.Query.Metrics;
using Veyra.Query.Resources;
using Veyra.Query.Server;
using Veyra.Query.ServerOverview.Api;
using Veyra.Query.Storage;
using Veyra.Query.Systems;
using PermissionsResponse = Veyra.Query.Server.Api.PermissionsResponse;
using VolumeResponse = Veyra.Query.Storage.Api.VolumeResponse;

namespace Veyra.Query.ServerOverview;

/// <summary>
/// Builds a SQL Server's Overview tab in one read (ADR 0025), as the estate's Overview is built (ADR 0023): it finds
/// the server's series once, then reads the lines over the window and the period before it, the values now, the sizes
/// of the last day and month, the volumes of the last week, the newest live read, and the newest events and
/// configuration, all at the same time.
/// </summary>
/// <remarks>
/// Every read picks its level by its buckets (ADR 0015): the lines read the rollups beyond six hours, and the month of
/// sizes reads one row per series and day, so the answer does not grow with the length of the window.
/// </remarks>
/// <param name="search">The metric series and points.</param>
/// <param name="activity">The live reads and the records of the server.</param>
/// <param name="thresholds">The backup rules' thresholds, for which databases the backup figure judges.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class ServerOverviewBuilder(
    IMetricSearch search,
    IDatabaseActivitySearch activity,
    IBackupThresholds thresholds,
    TimeProvider clock)
{
    private const string _cpu = "sqlserver.cpu.sql.ratio";

    private const string _osMemory = "sqlserver.os.memory.total";

    private const string _info = "sqlserver.server.info";

    private const string _compilations = "sqlserver.batch.sql_compilation.rate";

    private const string _io = "sqlserver.database.io";

    private const string _volumeSize = "sqlserver.volume.size";

    private const string _volumeFree = "sqlserver.volume.free";

    private const string _state = "sqlserver.database.offline";

    private const int _topWaits = 5;

    private const int _largest = 8;

    // One series per wait type, file, and database; room for a server with a few hundred databases.
    private const int _maxSeries = 10_000;

    // How old a live read may be for its chains to count as now, as on the Live tab.
    private static readonly TimeSpan _liveAge = LiveViewBuilder.MaxAge;

    // How far back the newest live read, event, and configuration are looked for, for when an area last sent.
    private static readonly TimeSpan _lookBack = TimeSpan.FromDays(7);

    private static readonly string[] _lineNames =
    [
        _cpu, SqlServerNames.Memory, _osMemory, SqlServerNames.Connections, SqlServerNames.Blocked,
        SqlServerNames.DatabaseSize, SqlServerNames.BatchRequests, _compilations, SqlServerNames.PageLife,
        SqlServerNames.WaitTime, _io, _volumeSize, _volumeFree, SqlServerNames.DeadlockRate,
    ];

    private static readonly string[] _recentNames =
    [
        _cpu, SqlServerNames.Memory, _osMemory, SqlServerNames.Connections, SqlServerNames.Blocked,
        SqlServerNames.DatabaseSize, SqlServerNames.BackupAge, SqlServerNames.BatchRequests, _volumeSize, _volumeFree,
        _info, SqlServerNames.Cpus, _state, PermissionsViewBuilder.MissingName,
    ];

    private static readonly string[] _names =
    [
        .. _lineNames.Concat(_recentNames).Concat(ServerCoverage.Names).Distinct(StringComparer.Ordinal),
    ];

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds the Overview of a server.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="system">Whether the server's own databases are listed in inventory and storage (VEY-66).</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The Overview, or <see langword="null"/> when the server sent neither metrics nor live reads.</returns>
    public async Task<ServerOverviewResponse?> OverviewAsync(
        string engine,
        string server,
        MetricSpan span,
        bool system,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(span, clock.GetUtcNow());
        var both = window.AndBefore();
        var end = window.End;
        var block = TelemetryBlock.Database(engine, server);
        var seenSince = both.ReadStart < end.AddDays(-30) ? both.ReadStart : end.AddDays(-30);

        var finding = search.FindSeriesAsync(
            new MetricSeriesQuery(seenSince, Names: _names, Limit: _maxSeries, Block: block),
            cancellationToken);
        var reading = activity.GetSessionsAsync(block.Name, end, _lookBack, cancellationToken);
        var events = NewestAsync(block, ["sqlserver.event", "sqlserver.deadlock"], end, cancellationToken);
        var configuration = NewestAsync(block, ["sqlserver.snapshot"], end, cancellationToken);

        await Task.WhenAll(finding, reading, events, configuration);

        var series = await finding;
        var snapshot = await reading;

        if (series.Count == 0 && snapshot is null)
        {
            return null;
        }

        List<MetricSeries> Named(params string[] names) =>
            series.Where(one => names.Contains(one.Name, StringComparer.Ordinal)).ToList();

        // Idle waits are many and never shown, so they are not read at all.
        var lineSeries = Named(_lineNames)
            .Where(one => one.Name != SqlServerNames.WaitTime
                || !SystemObjects.IsIdleWait(MetricNames.Attribute(one, "wait.type")))
            .ToList();
        var lines = _reader.ReadAsync(lineSeries, both, cancellationToken);
        var recent = _reader.ReadAsync(
            Named(_recentNames),
            MetricWindow.Covering(end.AddMinutes(-30), end, 30),
            cancellationToken);
        var day = _reader.ReadAsync(Named(SqlServerNames.DatabaseSize), MetricWindow.Day(end), cancellationToken);
        var month = _reader.ReadAsync(
            Named(SqlServerNames.DatabaseSize),
            MetricWindow.Covering(end.AddDays(-30), end, 30),
            cancellationToken);
        var week = _reader.ReadAsync(
            Named(_volumeFree),
            MetricWindow.Covering(end.AddDays(-7), end, 7),
            cancellationToken);

        await Task.WhenAll(lines, recent, day, month, week);

        var reads = new ServerOverviewReads(
            window,
            await lines,
            await recent,
            await day,
            await month,
            await week,
            series,
            snapshot,
            await events,
            await configuration);

        return Build(reads, thresholds.Current, system);
    }

    /// <summary>Turns the reads into the Overview. Pure; tested without a database.</summary>
    /// <param name="reads">The reads.</param>
    /// <param name="thresholds">The backup rules' thresholds.</param>
    /// <param name="system">Whether the server's own databases are listed.</param>
    /// <returns>The Overview.</returns>
    public static ServerOverviewResponse Build(ServerOverviewReads reads, BackupThresholds thresholds, bool system)
    {
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(thresholds);

        var window = reads.Window;
        var lines = reads.Lines;
        var recent = reads.Recent;

        var cpu = Positive(lines.Levels(lines.Named(_cpu)));
        var memory = Scaled(lines.Levels(lines.Named(SqlServerNames.Memory)), 1024);
        var machineMemory = lines.Levels(lines.Named(_osMemory));
        var sessions = lines.Levels(lines.Named(SqlServerNames.Connections));
        var blocked = lines.Levels(lines.Named(SqlServerNames.Blocked));
        var size = lines.Levels(lines.Named(SqlServerNames.DatabaseSize));
        var requests = lines.Rates(lines.Named(SqlServerNames.BatchRequests));
        var compilations = lines.Rates(lines.Named(_compilations));
        var pageLife = lines.Levels(lines.Named(SqlServerNames.PageLife));
        var waits = Scaled(
            lines.Rates(lines.Named(SqlServerNames.WaitTime)
                .Where(one => !SystemObjects.IsIdleWait(MetricNames.Attribute(one, "wait.type")))),
            1000);
        var volumeFree = FullestFree(lines);

        var memoryNow = Latest(recent, SqlServerNames.Memory) * 1024;
        var machineMemoryNow = Latest(recent, _osMemory);
        var memoryRatio = memory
            .Select(pair => (
                pair.Key,
                Total: machineMemory.TryGetValue(pair.Key, out var total) ? total : machineMemoryNow))
            .Where(pair => pair.Total is > 0)
            .ToDictionary(pair => pair.Key, pair => memory[pair.Key] / pair.Total!.Value);

        var volumes = StorageViewBuilder.Volumes(recent, reads.Week);
        var fullest = volumes
            .Where(volume => volume.SizeBytes > 0)
            .MinBy(volume => volume.FreeBytes / volume.SizeBytes);
        var inventory = Inventory(recent, reads.Day, system);

        return new ServerOverviewResponse(
            window.From,
            window.End,
            Machine(reads, inventory.Count),
            new ServerFiguresResponse(
                Figure(window, cpu, Positive(Latest(recent, _cpu)), ratio: true),
                Figure(window, memory, memoryNow, ratio: false),
                Figure(window, sessions, Latest(recent, SqlServerNames.Connections), ratio: false),
                Figure(window, blocked, Latest(recent, SqlServerNames.Blocked), ratio: false),
                Figure(window, size, Latest(recent, SqlServerNames.DatabaseSize), ratio: false),
                new FigureResponse(OldestFullBackup(recent, thresholds), [], null),
                Figure(
                    window,
                    requests,
                    recent.Latest(recent.Rates(recent.Named(SqlServerNames.BatchRequests))),
                    ratio: false),
                VolumeFigure(window, volumeFree, fullest)),
            new PerformanceResponse(
                (int)window.Bucket.TotalSeconds,
                window.Starts,
                Line(window, cpu),
                Line(window, memoryRatio),
                Line(window, waits)),
            [
                Counter("batchRequests", window, requests, "per second"),
                Counter("compilations", window, compilations, "per second"),
                Counter("pageLife", window, pageLife, "seconds"),
                Counter("waits", window, waits, "ms per second"),
            ],
            Io(window, lines),
            Waits(window, lines),
            StorageSummary(reads, volumes, system),
            inventory,
            Blocking(reads, blocked),
            ServerCoverage.Build(reads, Missing(recent)));
    }

    private static MachineResponse Machine(ServerOverviewReads reads, int databases)
    {
        var recent = reads.Recent;
        var info = recent.Named(_info).Where(one => recent.LastPoint([one]) is not null).MaxBy(one => one.LastSeen);
        var uptime = info is null ? null : recent.Latest(recent.Levels([info]));
        var at = info is null ? null : recent.LastPoint([info]);
        var permissions = PermissionsOf(recent);

        string? Text(string key) =>
            info is not null && MetricNames.Attribute(info, key) is { Length: > 0 } value ? value : null;

        return new MachineResponse(
            Text("version"),
            Text("update_level"),
            Text("edition"),
            Text("os_name"),
            Text("os_release"),
            Latest(recent, SqlServerNames.Cpus) is { } cpus ? (int)cpus : null,
            Latest(recent, _osMemory) is { } memory ? (long)memory : null,
            uptime is { } seconds && at is { } point
                ? new DateTimeOffset(point.UtcTicks - (point.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero)
                    .AddSeconds(-Math.Round(seconds))
                : null,
            uptime is { } value ? (long)Math.Round(value) : null,
            databases,
            permissions.Platform);
    }

    // The value now, the line, and the window's average against the period before's.
    private static FigureResponse Figure(
        MetricWindow window,
        IReadOnlyDictionary<DateTimeOffset, double> values,
        double? now,
        bool ratio)
    {
        var (current, previous) = Periods(window, values);

        return new FigureResponse(
            now,
            Line(window, values),
            ratio ? ResourceComparison.Points(current, previous) : ResourceComparison.Rate(current, previous));
    }

    private static VolumeFigureResponse VolumeFigure(
        MetricWindow window,
        IReadOnlyDictionary<DateTimeOffset, double> values,
        VolumeResponse? fullest)
    {
        var (current, previous) = Periods(window, values);

        return new VolumeFigureResponse(
            fullest is null ? null : fullest.FreeBytes / fullest.SizeBytes,
            Line(window, values),
            ResourceComparison.Points(current, previous),
            fullest?.Volume);
    }

    private static CounterResponse Counter(
        string name,
        MetricWindow window,
        IReadOnlyDictionary<DateTimeOffset, double> values,
        string unit)
    {
        var (current, previous) = Periods(window, values);

        return new CounterResponse(name, current, unit, ResourceComparison.Rate(current, previous));
    }

    // Each volume's free share per bucket, the lowest of them: the fullest volume, whichever it was then.
    private static Dictionary<DateTimeOffset, double> FullestFree(SeriesData lines)
    {
        var shares = lines.Named(_volumeSize)
            .GroupBy(one => MetricNames.Attribute(one, "volume") ?? "", StringComparer.Ordinal)
            .Select(group =>
            {
                var sizes = lines.Levels(group);
                var frees = lines.Levels(lines.Named(_volumeFree)
                    .Where(one => (MetricNames.Attribute(one, "volume") ?? "") == group.Key));

                return sizes
                    .Where(pair => pair.Value > 0 && frees.ContainsKey(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => frees[pair.Key] / pair.Value);
            });

        return shares
            .SelectMany(share => share)
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Min(pair => pair.Value));
    }

    private static IoResponse Io(MetricWindow window, SeriesData lines)
    {
        Dictionary<DateTimeOffset, double> Direction(string direction) =>
            lines.Rates(lines.Named(_io).Where(one => MetricNames.Attribute(one, "direction") == direction));

        var reads = Direction("read");
        var writes = Direction("write");

        return new IoResponse(
            Periods(window, reads).Current,
            Periods(window, writes).Current,
            Line(window, reads),
            Line(window, writes));
    }

    // The seconds each wait type waited within the window, the five longest and the rest as Other.
    private static WaitsSummaryResponse Waits(MetricWindow window, SeriesData lines)
    {
        var seconds = window.Bucket.TotalSeconds;
        var types = lines.Named(SqlServerNames.WaitTime)
            .Where(one => !SystemObjects.IsIdleWait(MetricNames.Attribute(one, "wait.type")))
            .GroupBy(one => MetricNames.Attribute(one, "wait.type") ?? "", StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0)
            .Select(group => (
                Type: group.Key,
                Category: group.Select(one => MetricNames.Attribute(one, "wait.category")).FirstOrDefault(),
                Seconds: lines.Rates(group).Where(pair => pair.Key >= window.Start).Sum(pair => pair.Value) * seconds))
            .Where(wait => wait.Seconds > 0)
            .OrderByDescending(wait => wait.Seconds)
            .ThenBy(wait => wait.Type, StringComparer.Ordinal)
            .ToList();
        var total = types.Sum(wait => wait.Seconds);
        var top = types.Take(_topWaits).ToList();
        var rest = types.Skip(_topWaits).Sum(wait => wait.Seconds);

        if (rest > 0)
        {
            top.Add(("Other", null, rest));
        }

        return new WaitsSummaryResponse(
            total,
            top.Select(wait => new WaitTypeShareResponse(
                    wait.Type,
                    wait.Category,
                    wait.Seconds,
                    total > 0 ? wait.Seconds / total : 0))
                .ToList());
    }

    private static StorageSummaryResponse StorageSummary(
        ServerOverviewReads reads,
        IReadOnlyList<VolumeResponse> volumes,
        bool system)
    {
        var recent = reads.Recent;
        var month = reads.Month;
        var totals = month.Levels(month.Named(SqlServerNames.DatabaseSize));
        var last = month.Window.Starts.Count == 0 ? (DateTimeOffset?)null : month.Window.Starts[^1];

        // What a set of sizes grew by over some days: the last day's size against the size that many days before.
        long? Grown(IReadOnlyDictionary<DateTimeOffset, double> sizes, int days) =>
            last is { } newest
            && sizes.TryGetValue(newest, out var now)
            && sizes.TryGetValue(newest.AddDays(-days), out var before)
                ? (long)Math.Round(now - before)
                : null;

        var largest = recent.Named(SqlServerNames.DatabaseSize)
            .GroupBy(one => MetricNames.Attribute(one, SqlServerNames.DatabaseAttribute) ?? "", StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0 && Listed(group.Key, system))
            .Select(group => (Database: group.Key, Bytes: recent.Latest(recent.Levels(group))))
            .Where(database => database.Bytes is not null)
            .OrderByDescending(database => database.Bytes)
            .ThenBy(database => database.Database, StringComparer.Ordinal)
            .Take(_largest)
            .Select(database =>
            {
                var own = month.Levels(month.Named(SqlServerNames.DatabaseSize)
                    .Where(one => MetricNames.Attribute(one, SqlServerNames.DatabaseAttribute) == database.Database));

                return new LargestDatabaseResponse(
                    database.Database,
                    (long)Math.Round(database.Bytes!.Value),
                    Whole(DatabaseView.Growth(reads.Day, database.Database)),
                    Grown(own, 7));
            })
            .ToList();

        return new StorageSummaryResponse(
            Whole(Latest(recent, SqlServerNames.DatabaseSize)),
            Whole(DayGrowth(reads.Day)),
            Grown(totals, 7),
            Grown(totals, 30),
            month.Window.Starts
                .Select(start => totals.TryGetValue(start, out var bytes) ? Whole(bytes) : null)
                .ToList(),
            largest,
            volumes
                .Select(volume => new OverviewVolumeResponse(
                    volume.Volume,
                    (long)volume.SizeBytes,
                    (long)volume.FreeBytes,
                    volume.SizeBytes > 0 ? volume.FreeBytes / volume.SizeBytes : 0,
                    volume.DaysUntilFull))
                .ToList());
    }

    // What every database together grew per day, from the first and last total of the day read.
    private static double? DayGrowth(SeriesData day)
    {
        var totals = day.Levels(day.Named(SqlServerNames.DatabaseSize));

        if (totals.Count < 2)
        {
            return null;
        }

        var first = totals.MinBy(pair => pair.Key);
        var last = totals.MaxBy(pair => pair.Key);
        var days = (last.Key - first.Key).TotalDays;

        return days > 0 ? (last.Value - first.Value) / days : null;
    }

    private static List<InventoryItemResponse> Inventory(SeriesData recent, SeriesData day, bool system)
    {
        var sizes = recent.Named(SqlServerNames.DatabaseSize).ToList();
        var states = recent.Named(_state)
            .GroupBy(one => MetricNames.Attribute(one, SqlServerNames.DatabaseAttribute) ?? "", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.MaxBy(one => one.LastSeen)!, StringComparer.Ordinal);

        double? Size(string database, string type) =>
            recent.Latest(recent.Levels(sizes.Where(one =>
                MetricNames.Attribute(one, SqlServerNames.DatabaseAttribute) == database
                && MetricNames.Attribute(one, "file_type") == type)));

        string? State(string database, string key) =>
            states.TryGetValue(database, out var state) && MetricNames.Attribute(state, key) is { Length: > 0 } value
                ? value
                : null;

        return sizes
            .Select(one => MetricNames.Attribute(one, SqlServerNames.DatabaseAttribute))
            .Concat(states.Keys)
            .OfType<string>()
            .Where(database => database.Length > 0 && Listed(database, system))
            .Distinct(StringComparer.Ordinal)
            .Select(database => (
                Database: database,
                Data: Size(database, "data"),
                Log: Size(database, "log")))
            .OrderByDescending(database => (database.Data ?? 0) + (database.Log ?? 0))
            .ThenBy(database => database.Database, StringComparer.Ordinal)
            .Select(database => new InventoryItemResponse(
                database.Database,
                Whole(database.Data),
                Whole(database.Log),
                Whole(DatabaseView.Growth(day, database.Database)),
                State(database.Database, "state"),
                State(database.Database, "recovery_model")))
            .ToList();
    }

    private static BlockingSummaryResponse Blocking(
        ServerOverviewReads reads,
        IReadOnlyDictionary<DateTimeOffset, double> blocked)
    {
        var window = reads.Window;
        var snapshot = reads.Snapshot;
        var current = snapshot is not null && window.End - snapshot.SampledAt <= _liveAge ? snapshot.Sessions : [];
        var byId = current
            .GroupBy(session => session.SessionId)
            .ToDictionary(group => group.Key, group => group.First());

        BlockedSessionResponse Waiter(int id) =>
            byId.TryGetValue(id, out var session)
                ? new BlockedSessionResponse(id, Empty(session.WaitType), session.WaitMilliseconds / 1000d)
                : new BlockedSessionResponse(id, null, null);

        var chains = BlockingTrees.Build(current)
            .Select(tree =>
            {
                var head = byId.GetValueOrDefault(tree.Session);

                return new BlockingChainResponse(
                    tree.Session,
                    Empty(head?.Database),
                    Empty(head?.Login),
                    Empty(head?.Host),
                    Empty(head?.WaitType),
                    head is { WaitType.Length: > 0 } waiting ? waiting.WaitMilliseconds / 1000d : null,
                    tree.Waiting,
                    Empty(head?.Text),
                    Behind(tree).Select(Waiter).ToList());
            })
            .ToList();
        var deadlocks = DatabaseView.Deadlocks(reads.Lines)
            .Where(pair => pair.Key >= window.Start)
            .Sum(pair => pair.Value);

        return new BlockingSummaryResponse(chains, Line(window, blocked), (int)Math.Round(deadlocks));
    }

    // Every session behind a head, the nearest first.
    private static IEnumerable<int> Behind(BlockingNodeResponse head)
    {
        var queue = new Queue<BlockingNodeResponse>(head.Children);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            yield return node.Session;

            foreach (var child in node.Children)
            {
                queue.Enqueue(child);
            }
        }
    }

    // The oldest last full backup among the user databases the backup rule judges.
    private static double? OldestFullBackup(SeriesData recent, BackupThresholds thresholds)
    {
        var ages = DatabaseView.LatestBy(recent, SqlServerNames.BackupAge, SqlServerNames.DatabaseAttribute)
            .Where(pair => pair.Value >= 0
                && !SystemObjects.IsSystemDatabase(SqlServerNames.Engine, pair.Key)
                && !thresholds.Ignores(pair.Key))
            .Select(pair => pair.Value)
            .ToList();

        return ages.Count == 0 ? null : ages.Max();
    }

    private static PermissionsResponse PermissionsOf(SeriesData recent)
    {
        var missing = DatabaseView.LatestBy(recent, PermissionsViewBuilder.MissingName, "permission");

        string? First(string key) =>
            recent.Named(PermissionsViewBuilder.MissingName)
                .Select(one => MetricNames.Attribute(one, key))
                .FirstOrDefault(value => value is not null);

        return PermissionsViewBuilder.Build(missing, First("login"), First("platform"));
    }

    private static HashSet<string> Missing(SeriesData recent) =>
        PermissionsOf(recent).Gaps.Select(gap => gap.Permission).ToHashSet(StringComparer.Ordinal);

    // The average of the window's buckets, and of the period before's.
    private static (double? Current, double? Previous) Periods(
        MetricWindow window,
        IReadOnlyDictionary<DateTimeOffset, double> values)
    {
        var current = values.Where(pair => pair.Key >= window.Start).Select(pair => pair.Value).ToList();
        var previous = values.Where(pair => pair.Key < window.Start).Select(pair => pair.Value).ToList();

        return (current.Count == 0 ? null : current.Average(), previous.Count == 0 ? null : previous.Average());
    }

    private static List<double?> Line(MetricWindow window, IReadOnlyDictionary<DateTimeOffset, double> values) =>
        window.Starts
            .Select(start => values.TryGetValue(start, out var value) && double.IsFinite(value) ? value : (double?)null)
            .ToList();

    private static double? Latest(SeriesData data, string name) => data.Latest(data.Levels(data.Named(name)));

    private static Dictionary<DateTimeOffset, double> Scaled(Dictionary<DateTimeOffset, double> values, double by) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value * by);

    // The agent sends -1 for a share it could not read.
    private static Dictionary<DateTimeOffset, double> Positive(Dictionary<DateTimeOffset, double> values) =>
        values.Where(pair => pair.Value >= 0).ToDictionary(pair => pair.Key, pair => pair.Value);

    private static double? Positive(double? value) => value is >= 0 ? value : null;

    private static bool Listed(string database, bool system) =>
        system || !SystemObjects.IsSystemDatabase(SqlServerNames.Engine, database);

    private static long? Whole(double? value) =>
        value is { } number && double.IsFinite(number) ? (long)Math.Round(number) : null;

    private static string? Empty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    // The newest record of some kinds within the look-back, for when an area last sent.
    private async Task<DateTimeOffset?> NewestAsync(
        TelemetryBlock block,
        string[] kinds,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? newest = null;

        foreach (var kind in kinds)
        {
            var records = await activity.ListRecordsAsync(
                block.Name,
                kind,
                end - _lookBack,
                end,
                null,
                1,
                cancellationToken);

            if (records.Count > 0 && (newest is null || records[0].SampledAt > newest))
            {
                newest = records[0].SampledAt;
            }
        }

        return newest;
    }
}
