using System.Globalization;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;
using Veyra.Query.Queries.Api;
using Veyra.Query.Storage.Api;

namespace Veyra.Query.Storage;

/// <summary>
/// Builds a SQL Server's databases and volumes in full (ADR 0020, areas 6 to 8) from the metrics of the agent's storage
/// read, with Veyra's findings and the statement that fixes each, for a DBA to run.
/// </summary>
/// <param name="search">The metric series and points.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class StorageViewBuilder(IMetricSearch search, TimeProvider clock)
{
    private static readonly string[] _names =
    [
        "sqlserver.database.offline",
        "sqlserver.log.used.ratio",
        "sqlserver.log.vlf.count",
        "sqlserver.file.size",
        "sqlserver.volume.size",
        "sqlserver.volume.free",
        "sqlserver.volume.used.ratio",
        "sqlserver.database.log_backup.age",
        "sqlserver.database.diff_backup.age",
        "sqlserver.database.checkdb.age",
        "sqlserver.database.suspect_pages",
        "sqlserver.database.log_chain.broken",
        "sqlserver.database.backup.duration",
        "sqlserver.database.backup.size",
        SqlServerNames.BackupAge,
    ];

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds the databases and volumes of a server.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The databases and volumes, or <see langword="null"/> when the server sent no storage read.</returns>
    public async Task<StorageResponse?> StorageAsync(string engine, string server, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var block = TelemetryBlock.Database(engine, server);
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(now.AddDays(-7), Names: _names, Limit: 5_000, Block: block),
            cancellationToken);

        if (series.Count == 0)
        {
            return null;
        }

        var recentReading = _reader.ReadAsync(
            series,
            MetricWindow.Covering(now.AddMinutes(-30), now, 30),
            cancellationToken);
        var weekReading = _reader.ReadAsync(
            series.Where(one => one.Name == "sqlserver.volume.free").ToList(),
            MetricWindow.Covering(now.AddDays(-7), now, 7),
            cancellationToken);

        await Task.WhenAll(recentReading, weekReading);

        return Build(series, await recentReading, await weekReading);
    }

    /// <summary>Turns the metrics into the response. Pure; tested without a database.</summary>
    /// <param name="series">The series seen in the last week.</param>
    /// <param name="recent">The last half hour.</param>
    /// <param name="week">The volumes' free space over the last week.</param>
    /// <returns>The databases and volumes.</returns>
    public static StorageResponse Build(IReadOnlyList<MetricSeries> series, SeriesData recent, SeriesData week)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(week);

        Dictionary<string, double> Latest(string name, string attribute = SqlServerNames.DatabaseAttribute) =>
            DatabaseView.LatestBy(recent, name, attribute);

        var logUsed = Latest("sqlserver.log.used.ratio");
        var vlfs = Latest("sqlserver.log.vlf.count");
        var fullAge = Latest(SqlServerNames.BackupAge);
        var diffAge = Latest("sqlserver.database.diff_backup.age");
        var logAge = Latest("sqlserver.database.log_backup.age");
        var checkdb = Latest("sqlserver.database.checkdb.age");
        var damaged = Latest("sqlserver.database.suspect_pages");
        var chain = Latest("sqlserver.database.log_chain.broken");
        var duration = Latest("sqlserver.database.backup.duration");
        var size = Latest("sqlserver.database.backup.size");
        var destination = series
            .Where(one => one.Name == "sqlserver.database.backup.size")
            .GroupBy(one => Attribute(one, SqlServerNames.DatabaseAttribute))
            .ToDictionary(group => group.Key, group => Attribute(group.MaxBy(one => one.LastSeen)!, "destination"));
        var files = series
            .Where(one => one.Name == "sqlserver.file.size")
            .GroupBy(one => (
                Database: Attribute(one, SqlServerNames.DatabaseAttribute),
                File: Attribute(one, "file_name")))
            .Select(group => group.MaxBy(one => one.LastSeen)!)
            .ToList();
        var fileSizes = DatabaseView.LatestBy(recent, "sqlserver.file.size", "file_name");

        var databases = series
            .Where(one => one.Name == "sqlserver.database.offline")
            .GroupBy(one => Attribute(one, SqlServerNames.DatabaseAttribute))
            .Select(group => group.MaxBy(one => one.LastSeen)!)
            .Select(state =>
            {
                var name = Attribute(state, SqlServerNames.DatabaseAttribute);
                var mine = files
                    .Where(file => Attribute(file, SqlServerNames.DatabaseAttribute) == name)
                    .Select(file => new DatabaseFileResponse(
                        Attribute(file, "file_name"),
                        Attribute(file, "file_type"),
                        Attribute(file, "volume"),
                        fileSizes.GetValueOrDefault(Attribute(file, "file_name")),
                        Attribute(file, "growth"),
                        Attribute(file, "max_size")))
                    .ToList();
                var database = new DatabaseStorageResponse(
                    name,
                    Attribute(state, "state"),
                    Attribute(state, "recovery_model"),
                    Attribute(state, "compatibility"),
                    Attribute(state, "page_verify"),
                    Attribute(state, "auto_close") == "on",
                    Attribute(state, "auto_shrink") == "on",
                    Attribute(state, "read_committed_snapshot") == "on",
                    Attribute(state, "encrypted") == "on",
                    Attribute(state, "log_reuse_wait"),
                    Value(logUsed, name),
                    Value(vlfs, name),
                    mine,
                    Value(fullAge, name),
                    Value(diffAge, name),
                    Value(logAge, name),
                    Value(chain, name) >= 1,
                    Value(duration, name),
                    Value(size, name),
                    destination.GetValueOrDefault(name) is { Length: > 0 } where ? where : null,
                    Value(checkdb, name),
                    Value(damaged, name) ?? 0,
                    []);

                return database with { Findings = Findings(database) };
            })
            .OrderBy(database => database.Findings.Count == 0)
            .ThenBy(database => database.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var volumes = Latest("sqlserver.volume.size", "volume")
            .Select(pair =>
            {
                var free = Latest("sqlserver.volume.free", "volume").GetValueOrDefault(pair.Key);
                var levels = week.Where(one => Attribute(one, "volume") == pair.Key);
                var points = levels.Levels(levels.Named("sqlserver.volume.free")).OrderBy(point => point.Key).ToList();
                double? days = null;

                if (points.Count >= 2)
                {
                    var spanDays = (points[^1].Key - points[0].Key).TotalDays;
                    var perDay = spanDays > 0 ? (points[0].Value - points[^1].Value) / spanDays : 0;
                    days = perDay > 0 ? free / perDay : null;
                }

                return new VolumeResponse(
                    pair.Key,
                    pair.Value,
                    free,
                    pair.Value > 0 ? 1 - (free / pair.Value) : 0,
                    days);
            })
            .OrderBy(volume => volume.Volume, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StorageResponse(databases, volumes, ServerFindings(databases, volumes));
    }

    /// <summary>What Veyra makes of one database, the worst first. Pure; tested without a database.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The findings.</returns>
    public static IReadOnlyList<PlanWarningResponse> Findings(DatabaseStorageResponse database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var name = database.Name;
        var quoted = $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
        var findings = new List<PlanWarningResponse>();

        if (database.State is { Length: > 0 } state && state != "ONLINE")
        {
            findings.Add(
                new(
                    "not-online",
                    "warning",
                    $"{name} is {state.ToLowerInvariant()}",
                    "Nobody can use it until it is online again.",
                    "Look at SQL Server's error log for why.",
                    null));
        }

        if (database.DamagedPages > 0)
        {
            findings.Add(
                new(
                    "damaged-pages",
                    "warning",
                    $"{name} has {Count(database.DamagedPages, "damaged page")}",
                    "SQL Server read pages that failed their checksum. The disk or its controller may be failing.",
                    "Run DBCC CHECKDB now and restore the damaged pages from a backup.",
                    $"DBCC CHECKDB ({quoted}) WITH NO_INFOMSGS, ALL_ERRORMSGS;"));
        }

        if (database.LogChainBroken)
        {
            findings.Add(
                new(
                    "log-chain",
                    "warning",
                    $"The log backups of {name} have a gap",
                    "A restore cannot go past the gap, so the changes after it can be lost.",
                    "Take a full backup to start a new chain, then find what broke it, such as a switch to simple "
                    + "recovery.",
                    null));
        }

        if (database.LogUsedRatio is >= 0.5
            && database.LogReuseWait is { Length: > 0 } wait
            && wait is not "NOTHING" and not "CHECKPOINT")
        {
            findings.Add(
                new(
                    "log-reuse",
                    database.LogUsedRatio >= 0.8 ? "warning" : "caution",
                    $"The log of {name} is {Percent(database.LogUsedRatio.Value)} in use and waits for "
                    + $"{wait.Replace('_', ' ').ToLowerInvariant()}",
                    "SQL Server cannot reuse the log until what it waits for happens; until then the log grows.",
                    wait == "LOG_BACKUP"
                        ? "Take a log backup, and schedule them."
                        : "Find and end what holds it, such as an open transaction or a lagging replica.",
                    null));
        }

        if (database.VirtualLogFiles is >= 1000)
        {
            findings.Add(
                new(
                    "vlf",
                    "caution",
                    $"The log of {name} has {Count(database.VirtualLogFiles.Value, "virtual log file")}",
                    "Many small growths left many pieces, which slow start-up and restores.",
                    "Shrink the log once and grow it again in large steps, with a fixed growth.",
                    null));
        }

        if (database.CheckdbAgeSeconds is >= 8 * 86400)
        {
            findings.Add(
                new(
                    "checkdb",
                    "caution",
                    $"{name} has had no good integrity check for "
                    + $"{Count(Math.Floor(database.CheckdbAgeSeconds.Value / 86400), "day")}",
                    "Damage found late may be in every backup kept.",
                    "Schedule DBCC CHECKDB weekly, for example in a SQL Agent job.",
                    $"DBCC CHECKDB ({quoted}) WITH NO_INFOMSGS;"));
        }

        if (database.AutoShrink)
        {
            findings.Add(
                new(
                    "auto-shrink",
                    "caution",
                    $"Auto shrink is on for {name}",
                    "Shrinking moves pages around, fragments indexes, and the file grows again soon after.",
                    "Turn it off.",
                    $"ALTER DATABASE {quoted} SET AUTO_SHRINK OFF;"));
        }

        if (database.AutoClose)
        {
            findings.Add(
                new(
                    "auto-close",
                    "caution",
                    $"Auto close is on for {name}",
                    "The database closes when the last connection leaves and is opened again for the next, which "
                    + "costs time each time.",
                    "Turn it off.",
                    $"ALTER DATABASE {quoted} SET AUTO_CLOSE OFF;"));
        }

        if (database.PageVerify is { Length: > 0 } verify && verify != "CHECKSUM")
        {
            findings.Add(
                new(
                    "page-verify",
                    "caution",
                    $"{name} checks pages with {verify.ToLowerInvariant()}, not checksum",
                    "Damaged pages may go unnoticed.",
                    "Use checksum; pages get one as they are written again.",
                    $"ALTER DATABASE {quoted} SET PAGE_VERIFY CHECKSUM;"));
        }

        foreach (var file in database.Files.Where(file => file.Growth.EndsWith('%')))
        {
            findings.Add(
                new(
                    "percent-growth",
                    "caution",
                    $"{file.Name} of {name} grows by {file.Growth}",
                    "Each growth is larger than the last, and log growths are never instant, so a large one stalls "
                    + "the database.",
                    "Grow it by a fixed size.",
                    $"ALTER DATABASE {quoted} MODIFY FILE (NAME = "
                    + $"[{file.Name.Replace("]", "]]", StringComparison.Ordinal)}], FILEGROWTH = "
                    + $"{(file.Type == "log" ? "512MB" : "1024MB")});"));
        }

        return findings.OrderBy(finding => finding.Severity == "warning" ? 0 : 1).ToList();
    }

    private static List<PlanWarningResponse> ServerFindings(
        IReadOnlyList<DatabaseStorageResponse> databases,
        IReadOnlyList<VolumeResponse> volumes)
    {
        var findings = new List<PlanWarningResponse>();
        var tempdb = databases.FirstOrDefault(database => database.Name == "tempdb");
        var data = tempdb?.Files.Where(file => file.Type == "data").ToList() ?? [];

        if (data.Count > 1 && data.Select(file => file.SizeBytes).Distinct().Count() > 1)
        {
            findings.Add(
                new(
                    "tempdb-files",
                    "caution",
                    "tempdb's data files differ in size",
                    "SQL Server fills the files by their free space, so the largest takes most of the work and its "
                    + "pages become a hot spot.",
                    "Give every tempdb data file the same size and growth.",
                    null));
        }

        foreach (var volume in volumes.Where(volume => volume.DaysUntilFull is < 30))
        {
            findings.Add(
                new(
                    "volume-filling",
                    volume.DaysUntilFull < 7 ? "warning" : "caution",
                    $"Volume {volume.Volume} fills in "
                    + $"{Count(Math.Max(1, Math.Floor(volume.DaysUntilFull!.Value)), "day")}",
                    "At the pace of the last week. When a volume is full, its databases stop taking changes.",
                    "Free space or add room to the volume, or move files.",
                    null));
        }

        return findings;
    }

    private static double? Value(Dictionary<string, double> values, string database) =>
        values.TryGetValue(database, out var value) && value >= 0 ? value : null;

    private static string Attribute(MetricSeries series, string key) => MetricNames.Attribute(series, key) ?? "";

    private static string Count(double count, string noun) =>
        count == 1 ? $"1 {noun}" : string.Create(CultureInfo.InvariantCulture, $"{count:N0} {noun}s");

    private static string Percent(double ratio) => string.Create(CultureInfo.InvariantCulture, $"{ratio * 100:0}%");
}
