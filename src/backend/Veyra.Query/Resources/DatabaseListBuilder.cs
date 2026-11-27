using System.Globalization;
using Microsoft.Extensions.Options;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Live;
using Veyra.Query.Metrics;
using Veyra.Query.Metrics.Api;
using Veyra.Query.Resources.Api;

namespace Veyra.Query.Resources;

/// <summary>
/// Builds the Databases list (ADR 0022): every database server whose metrics are kept, with its databases, their
/// sizes and backups, its blocking chains, and its health from the open alerts.
/// </summary>
/// <remarks>
/// The databases are worked out as the server's screen works them out (<see cref="DatabaseView"/>,
/// <see cref="PostgresView"/>), from the overview cards' series over the window, with the window standing in for the
/// last five minutes: sizes and backup ages are their newest value in it. The blocking chains come from each server's
/// newest live read of the window's last minute (ADR 0020), all servers in one read.
/// </remarks>
/// <param name="search">The stored metrics.</param>
/// <param name="alerts">The alerts, for health.</param>
/// <param name="activity">The live reads, for blocking chains.</param>
/// <param name="options">The Query settings, for when a server is silent.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class DatabaseListBuilder(
    IMetricSearch search,
    IAlertStates alerts,
    IDatabaseActivitySearch activity,
    IOptionsMonitor<QueryOptions> options,
    TimeProvider clock)
{
    private const string _serverInfo = "sqlserver.server.info";

    private const string _sqlCpu = "sqlserver.cpu.sql.ratio";

    private const string _tempdb = "tempdb";

    // One series per database from the agent's storage read, with the database's state as an attribute (ADR 0020).
    private const string _offline = "sqlserver.database.offline";

    // The agent reads the live view every 10 seconds; a read older than this is not the server's now.
    private static readonly TimeSpan _liveWithin = TimeSpan.FromMinutes(1);

    // A database per series of each per-database metric: room for thousands of databases.
    private const int _maxSeries = 50_000;

    private static readonly string[] _names =
        [.. SqlServerNames.Card, _sqlCpu, _serverInfo, _offline, .. MetricNames.PostgresScreen];

    private static readonly string[] _sizes = [SqlServerNames.DatabaseSize, MetricNames.PostgresDatabaseSize];

    private static readonly AlertBlockKind[] _kinds = [AlertBlockKind.Database];

    // SQL Server's major versions and the release each belongs to.
    private static readonly Dictionary<string, string> _releases = new(StringComparer.Ordinal)
    {
        ["11"] = "2012",
        ["12"] = "2014",
        ["13"] = "2016",
        ["14"] = "2017",
        ["15"] = "2019",
        ["16"] = "2022",
        ["17"] = "2025",
    };

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds one page of the list.</summary>
    /// <param name="query">The filters, sort, and page.</param>
    /// <param name="engine">Only servers of this engine, such as <c>microsoft.sql_server</c>; null for all.</param>
    /// <param name="system">Whether the servers' own databases, such as master, are counted.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page, or null when the cursor is not one Veyra issued.</returns>
    public async Task<DatabaseListResponse?> ListAsync(
        ResourceListQuery query,
        string? engine,
        bool system,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var listing = await ReadAsync(query, engine, system, cancellationToken);
        var page = ResourcePage.Of(listing.Servers, query.Cursor, query.Limit);

        if (page is null)
        {
            return null;
        }

        return new DatabaseListResponse(
            listing.Header,
            [.. page.Items.Select(server => server.Response)],
            page.Total,
            page.Next);
    }

    /// <summary>
    /// Builds the whole list before it is cut into pages, with every server's databases and their states, which the
    /// Overview lists across servers (ADR 0023).
    /// </summary>
    /// <param name="query">The filters and sort; the cursor and limit are not used.</param>
    /// <param name="engine">Only servers of this engine, such as <c>microsoft.sql_server</c>; null for all.</param>
    /// <param name="system">Whether the servers' own databases, such as master, are counted.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The sorted list.</returns>
    public async Task<DatabaseListing> ReadAsync(
        ResourceListQuery query,
        string? engine,
        bool system,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = clock.GetUtcNow();
        var window = MetricWindow.Card(query.Span, now);
        var silentSince = now - options.CurrentValue.SilentAfterHosts;
        var chosenEngine = string.IsNullOrWhiteSpace(engine) ? null : engine.Trim();

        var listing = search.ListBlocksAsync(_kinds, window.End, cancellationToken);
        var finding = search.FindSeriesAsync(
            new MetricSeriesQuery(
                window.Start,
                Names: _names,
                Environment: query.Chosen,
                Limit: _maxSeries,
                BlockNameContains: query.Needle,
                OrNoEnvironment: true),
            cancellationToken);
        var readingLive = activity.ListLatestSessionsAsync(window.End, _liveWithin, cancellationToken);
        var reading = alerts.ListAsync(now, cancellationToken);

        await Task.WhenAll(listing, finding, readingLive, reading);

        var health = new ResourceHealth(await reading);
        var found = (await finding).Where(one => one.Block?.Kind == AlertBlockKind.Database).ToList();

        // A database's state is an attribute of its series of the storage read; its points are not needed.
        var states = found.Where(one => one.Name == _offline).ToList();
        var series = found.Where(one => one.Name != _offline).ToList();
        var readingWindow = _reader.ReadAsync(series, window, cancellationToken);
        var readingDay = _reader.ReadAsync(
            series.Where(one => _sizes.Contains(one.Name)).ToList(),
            MetricWindow.Day(window.End),
            cancellationToken);

        await Task.WhenAll(readingWindow, readingDay);

        var data = await readingWindow;
        var day = await readingDay;
        var live = (await readingLive).GroupBy(read => read.Block).ToDictionary(
            group => group.Key,
            group => group.MaxBy(read => read.SampledAt)!);

        var items = (await listing)
            .GroupBy(block => block.Block)
            .Where(group => group.Any(block => query.Holds(block.Environment, everywhereWithout: true)))
            .Where(group => query.Matches(group.Key.Server ?? group.Key.Name))
            .Where(group => chosenEngine is null
                || string.Equals(group.Key.Engine, chosenEngine, StringComparison.OrdinalIgnoreCase))
            .Select(group => Server(
                group.Key,
                group.Max(block => block.LastSeen),
                data.Where(one => one.Block == group.Key),
                day.Where(one => one.Block == group.Key),
                live.GetValueOrDefault(group.Key.Name),
                health.Of(AlertBlockKind.Database, group.Key.Name, null),
                group.Max(block => block.LastSeen) < silentSince,
                system,
                StatesOf(states.Where(one => one.Block == group.Key))))
            .Where(item => query.Health.Keeps(item.Response.Health, item.Response.Silent))
            .ToList();

        var sorted = ResourceRanking.Sort(
            items,
            query.Sort,
            item => item.Response.Server,
            item => item.Response.Health,
            item => item.Response.Silent);

        return new DatabaseListing(sorted, Header(items));
    }

    // Each database's state, such as ONLINE, from its newest series of the storage read.
    private static Dictionary<string, string> StatesOf(IEnumerable<MetricSeries> series) =>
        series
            .Where(one => MetricNames.Attribute(one, SqlServerNames.DatabaseAttribute) is not null)
            .GroupBy(one => MetricNames.Attribute(one, SqlServerNames.DatabaseAttribute)!, StringComparer.Ordinal)
            .Select(group => (group.Key, State: MetricNames.Attribute(group.MaxBy(one => one.LastSeen)!, "state")))
            .Where(pair => !string.IsNullOrEmpty(pair.State))
            .ToDictionary(pair => pair.Key, pair => pair.State!, StringComparer.Ordinal);

    // One server's row, and its databases, the late ones with their ages, which the header needs.
    private static DatabaseServerListing Server(
        TelemetryBlock block,
        DateTimeOffset lastSeen,
        SeriesData data,
        SeriesData day,
        SessionSnapshot? live,
        Home.BlockHealth health,
        bool silent,
        bool system,
        IReadOnlyDictionary<string, string> states)
    {
        var sqlServer = block.Engine != MetricNames.PostgresEngine;
        var databases = (sqlServer ? DatabaseView.Databases(data, data, day) : PostgresView.Databases(data, day, []))
            .Where(database => system || !database.System)
            .ToList();
        var backedUp = databases.Where(database => database.Name != _tempdb).ToList();
        var late = sqlServer
            ? backedUp
                .Where(database => database.BackupAgeSeconds is not { } age
                    || age >= DatabaseOverview.LateBackup.TotalSeconds)
                .ToList()
            : [];

        return new DatabaseServerListing(
            block,
            new DatabaseItemResponse(
                block.Engine ?? block.Name,
                block.Server ?? block.Name,
                sqlServer ? Version(data) : null,
                health,
                silent,
                databases.Count,
                Total(databases.Select(database => database.SizeBytes)),
                Total(databases.Select(database => database.GrowthBytesPerDay)),
                sqlServer ? backedUp.Max(database => database.BackupAgeSeconds) : null,
                sqlServer && data.Series.Count > 0 ? late.Count : null,
                sqlServer && live is not null ? BlockingTrees.Build(live.Sessions).Count : null,
                sqlServer ? data.Average(data.Levels(data.Named(_sqlCpu))) : null),
            databases,
            late,
            states);
    }

    private static DatabaseListHeaderResponse Header(List<DatabaseServerListing> items)
    {
        var late = items
            .SelectMany(item => item.Late.Select(database => (item.Response.Server, Database: database)))
            .OrderByDescending(pair => pair.Database.BackupAgeSeconds ?? double.PositiveInfinity)
            .ThenBy(pair => pair.Server, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Database.Name, StringComparer.Ordinal)
            .ToList();
        var blocking = items
            .Where(item => item.Response.BlockingChains > 0)
            .OrderByDescending(item => item.Response.BlockingChains)
            .ThenBy(item => item.Response.Server, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return new DatabaseListHeaderResponse(
            items.Count,
            items.Count(item => item.Response.Silent),
            items.Sum(item => item.Response.Databases),
            Total(items.Select(item => item.Response.SizeBytes)),
            Total(items.Select(item => item.Response.GrowthBytesPerDay)),
            late.Count,
            late.Count == 0 ? null : new LateBackupResponse(late[0].Server, late[0].Database.Name),
            items.Sum(item => item.Response.BlockingChains ?? 0),
            blocking?.Response.Server);
    }

    // SQL Server's version as people name it, such as "SQL Server 2022 CU14", from the info series' attributes.
    private static string? Version(SeriesData data)
    {
        var info = data.Named(_serverInfo).MaxBy(one => one.LastSeen);
        var version = info is null ? null : MetricNames.Attribute(info, "version");

        if (version is null)
        {
            return null;
        }

        var major = version.Split('.')[0];
        var update = MetricNames.Attribute(info!, "update_level");
        var release = _releases.TryGetValue(major, out var year)
            ? string.Create(CultureInfo.InvariantCulture, $"SQL Server {year}")
            : $"SQL Server {version}";

        return string.IsNullOrWhiteSpace(update) ? release : $"{release} {update}";
    }

    private static double? Total(IEnumerable<double?> values)
    {
        var known = values.OfType<double>().ToList();

        return known.Count == 0 ? null : known.Sum();
    }
}
