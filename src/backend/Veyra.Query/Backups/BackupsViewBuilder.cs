using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Backups.Api;
using Veyra.Query.Metrics;
using Veyra.Query.Server;
using Veyra.Query.Systems;

namespace Veyra.Query.Backups;

/// <summary>
/// Builds a SQL Server's Backups tab (ADR 0025) from the backups the agent reads from msdb, their counts per day, and
/// the backup ages of the storage read, judged by the backup rules' thresholds.
/// </summary>
/// <param name="search">The metric series and points.</param>
/// <param name="activity">The stored backups.</param>
/// <param name="history">The backups counted per day.</param>
/// <param name="thresholds">The backup rules' thresholds, as saved on the Settings screen.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class BackupsViewBuilder(
    IMetricSearch search,
    IDatabaseActivitySearch activity,
    IBackupHistory history,
    IBackupThresholds thresholds,
    TimeProvider clock)
{
    /// <summary>The kind of record each backup is kept as.</summary>
    public const string BackupKind = "sqlserver.backup";

    /// <summary>The most backups listed.</summary>
    public const int MaxBackups = 500;

    /// <summary>The days of the grid.</summary>
    public const int GridDays = 7;

    private const string _full = "full";

    private const string _differential = "differential";

    private const string _log = "log";

    private const string _fullAge = SqlServerNames.BackupAge;

    private const string _diffAge = "sqlserver.database.diff_backup.age";

    private const string _logAge = "sqlserver.database.log_backup.age";

    private const string _state = "sqlserver.database.offline";

    // The storage read runs every 5 minutes and the permissions with it; half an hour rides out a late one.
    private static readonly TimeSpan _recent = TimeSpan.FromMinutes(30);

    private static readonly string[] _names = [_fullAge, _diffAge, _logAge, _state, PermissionsViewBuilder.MissingName];

    private static readonly string[] _selfManaged = ["managed-instance", "azure-sql-database"];

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds the tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="system">Whether the server's own databases are listed (VEY-66).</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The tab, or <see langword="null"/> when the server sent neither backups nor backup ages.</returns>
    public async Task<BackupsResponse?> BackupsAsync(
        string engine,
        string server,
        MetricSpan span,
        bool system,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(span);

        var now = clock.GetUtcNow();
        var period = span.Resolve(now) ?? MetricSpan.FromString(null).Resolve(now)!;
        var block = TelemetryBlock.Database(engine, server);

        // Whole UTC days: the days of the window, and the grid's seven days before its end.
        var firstDay = Math.Min(Day(period.From).DayNumber, Day(period.To).AddDays(1 - GridDays).DayNumber);
        var countFrom = new DateTimeOffset(DateOnly.FromDayNumber(firstDay), TimeOnly.MinValue, TimeSpan.Zero);

        // The reads do not depend on each other, so they run at the same time (ADR 0015).
        var finding = search.FindSeriesAsync(
            new MetricSeriesQuery(period.To - TimeSpan.FromDays(1), Names: _names, Limit: 5_000, Block: block),
            cancellationToken);
        var listing = activity.ListRecordsAsync(
            block.Name,
            BackupKind,
            period.From,
            period.To,
            null,
            MaxBackups,
            cancellationToken);
        var counting = history.ListDaysAsync(block.Name, countFrom, period.To, cancellationToken);

        await Task.WhenAll(finding, listing, counting);

        var series = await finding;
        var records = await listing;
        var days = await counting;

        if (series.Count == 0 && records.Count == 0 && days.Count == 0)
        {
            return null;
        }

        var recent = await _reader.ReadAsync(
            series,
            MetricWindow.Covering(period.To - _recent, period.To, 30),
            cancellationToken);

        return Build(period.From, period.To, records, days, recent, thresholds.Current, system);
    }

    /// <summary>Turns the reads into the tab. Pure; tested without a database.</summary>
    /// <param name="from">The start of the window.</param>
    /// <param name="to">The end of the window.</param>
    /// <param name="records">The backups that finished in the window, the newest first.</param>
    /// <param name="days">The backups per day, database, and type, of the window's days and the grid's.</param>
    /// <param name="recent">The backup ages, states, and permissions of the last half hour.</param>
    /// <param name="thresholds">The backup rules' thresholds.</param>
    /// <param name="system">Whether the server's own databases are listed.</param>
    /// <returns>The tab.</returns>
    public static BackupsResponse Build(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<ActivityRecord> records,
        IReadOnlyList<BackupDay> days,
        SeriesData recent,
        BackupThresholds thresholds,
        bool system)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(thresholds);

        // tempdb cannot be backed up, so it is never listed; the server's other databases only when asked for.
        bool Listed(string database) =>
            database.Length > 0
            && !string.Equals(database, "tempdb", StringComparison.OrdinalIgnoreCase)
            && (system || !SystemObjects.IsSystemDatabase(SqlServerNames.Engine, database));

        var backups = records
            .Select(Backup)
            .OfType<BackupResponse>()
            .Where(backup => Listed(backup.Database))
            .OrderByDescending(backup => backup.FinishedAt)
            .ToList();
        var counted = days.Where(day => Listed(day.Database)).ToList();
        var freshness = Freshness(recent, thresholds).Where(database => Listed(database.Database)).ToList();
        var platform = recent.Named(PermissionsViewBuilder.MissingName)
            .Select(one => MetricNames.Attribute(one, "platform"))
            .FirstOrDefault(name => name is not null);

        return new BackupsResponse(
            from,
            to,
            platform is not null && _selfManaged.Contains(platform, StringComparer.Ordinal),
            backups,
            Days(counted, Day(from), Day(to)),
            Grid(counted, freshness.Select(database => database.Database), Day(to)),
            freshness);
    }

    // A record as the agent sent it; null for a type the tab does not show.
    private static BackupResponse? Backup(ActivityRecord record)
    {
        var type = Text(record, "backup_type");

        if (type is not (_full or _differential or _log))
        {
            return null;
        }

        var finished = record.SampledAt;
        var started = Number(record, "started") is { } milliseconds and > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds)
            : finished;

        return new BackupResponse(
            Text(record, "database_name"),
            type,
            started,
            finished,
            Math.Max(0, (finished - started).TotalSeconds),
            Size(record, "bytes"),
            Size(record, "compressed_bytes"),
            Text(record, "copy_only") == "1",
            record.Body.Length > 0 ? record.Body : null);
    }

    private static List<BackupDayResponse> Days(IReadOnlyList<BackupDay> days, DateOnly first, DateOnly last)
    {
        var byDay = days.ToLookup(day => day.Day);
        var result = new List<BackupDayResponse>();

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var own = byDay[day].ToList();

            result.Add(
                new BackupDayResponse(
                    Text(day),
                    Of(own, _full),
                    Of(own, _differential),
                    Of(own, _log)));
        }

        return result;
    }

    private static BackupTypeDayResponse Of(List<BackupDay> days, string type)
    {
        var own = days.Where(day => day.Type == type).ToList();

        return own.Count == 0
            ? BackupTypeDayResponse.None
            : new BackupTypeDayResponse(own.Sum(day => day.Count), own.Sum(day => day.Seconds));
    }

    // Every database the server reports or that had a backup in the grid's days, by name.
    private static BackupGridResponse Grid(IReadOnlyList<BackupDay> days, IEnumerable<string> known, DateOnly last)
    {
        var first = last.AddDays(1 - GridDays);
        var grid = Enumerable.Range(0, GridDays).Select(offset => first.AddDays(offset)).ToList();
        var inGrid = days.Where(day => day.Day >= first && day.Day <= last).ToList();
        var rows = known
            .Concat(inGrid.Select(day => day.Database))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(database => new BackupGridRowResponse(
                database,
                grid.Select(day =>
                    {
                        var own = inGrid.Where(one => one.Day == day && one.Database == database).ToList();

                        return new BackupGridCellResponse(
                            own.Any(one => one.Type == _full && one.Count > 0),
                            own.Any(one => one.Type == _differential && one.Count > 0),
                            own.Where(one => one.Type == _log).Sum(one => one.Count));
                    })
                    .ToList()))
            .ToList();

        return new BackupGridResponse(grid.Select(Text).ToList(), rows);
    }

    /// <summary>
    /// Each database's backup ages, judged by the backup rules as the alerts judge them: the full backup rule unless
    /// the database is not backed up on purpose, and the log backup rule outside simple recovery. A database that
    /// never had a full backup is a warning, as on the server's Overview. The worst first. Pure.
    /// </summary>
    /// <param name="recent">The backup ages and states of the last half hour.</param>
    /// <param name="thresholds">The backup rules' thresholds.</param>
    /// <returns>Every database the server reports but tempdb.</returns>
    public static List<BackupFreshnessResponse> Freshness(SeriesData recent, BackupThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(thresholds);

        var full = Ages(recent, _fullAge);
        var diff = Ages(recent, _diffAge);
        var log = Ages(recent, _logAge);
        var recovery = recent.Named(_state)
            .GroupBy(one => MetricNames.Attribute(one, SqlServerNames.DatabaseAttribute) ?? "", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => MetricNames.Attribute(group.MaxBy(one => one.LastSeen)!, "recovery_model"),
                StringComparer.Ordinal);

        return recovery.Keys
            .Concat(full.Keys)
            .Concat(log.Keys)
            .Where(database => database.Length > 0 && database != "tempdb")
            .Distinct(StringComparer.Ordinal)
            .Select(database => Judge(
                database,
                full.TryGetValue(database, out var fullAge) ? fullAge : null,
                diff.TryGetValue(database, out var diffAge) ? diffAge : null,
                log.TryGetValue(database, out var logAge) ? logAge : null,
                recovery.GetValueOrDefault(database),
                thresholds))
            .OrderBy(database => Rank(database.State))
            .ThenBy(database => database.Database, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static BackupFreshnessResponse Judge(
        string database,
        double? full,
        double? diff,
        double? log,
        string? recovery,
        BackupThresholds thresholds)
    {
        var ignored = thresholds.Ignores(database);
        var judgesFull = thresholds.FullJudged && !ignored;
        var judgesLog = thresholds.LogJudged && log is not null;
        var parts = new List<string>();
        var state = "ignored";

        if (judgesFull)
        {
            parts.Add($"full backup within {Duration(thresholds.FullCautionSeconds)}, "
                + $"warning after {Duration(thresholds.FullWarningSeconds)}");
            state = Worse(state, full is { } age
                ? Level(age, thresholds.FullCautionSeconds, thresholds.FullWarningSeconds)
                : "warning");
        }

        if (judgesLog)
        {
            parts.Add(thresholds.LogCautionSeconds is { } caution
                ? $"log backup within {Duration(caution)}, warning after {Duration(thresholds.LogWarningSeconds)}"
                : $"log backup warning after {Duration(thresholds.LogWarningSeconds)}");
            state = Worse(state, Level(log!.Value, thresholds.LogCautionSeconds, thresholds.LogWarningSeconds));
        }

        var policy = parts.Count > 0
            ? string.Join("; ", parts)
            : ignored
                ? "Not backed up on purpose, so the backup rule leaves it alone"
                : !thresholds.FullJudged && !thresholds.LogJudged
                    ? "No backup rule is on"
                    : "No backup rule judges it";

        return new BackupFreshnessResponse(
            database,
            full,
            diff,
            log,
            recovery,
            state,
            char.ToUpperInvariant(policy[0]) + policy[1..]);
    }

    private static string Level(double age, double? caution, double warning) =>
        age >= warning ? "warning" : caution is { } level && age >= level ? "caution" : "ok";

    private static string Worse(string current, string next) => Rank(next) < Rank(current) ? next : current;

    private static int Rank(string state) => state switch
    {
        "warning" => 0,
        "caution" => 1,
        "ok" => 2,
        _ => 3,
    };

    // The agent sends -1 for "none", such as no log backups in simple recovery.
    private static Dictionary<string, double> Ages(SeriesData recent, string name) =>
        DatabaseView.LatestBy(recent, name, SqlServerNames.DatabaseAttribute)
            .Where(pair => pair.Value >= 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    // Hours for an hour or more, minutes below, as the Settings screen shows them.
    private static string Duration(double seconds) =>
        seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(seconds / 3600, 1):0.#} h")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(seconds / 60):0} min");

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.UtcDateTime);

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Text(ActivityRecord record, string column) =>
        record.Attributes.TryGetValue(column, out var value) ? value : "";

    private static double? Number(ActivityRecord record, string column) =>
        double.TryParse(Text(record, column), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number)
            ? number
            : null;

    // msdb leaves a size it did not record NULL, which the agent sends as -1.
    private static long? Size(ActivityRecord record, string column) =>
        Number(record, column) is { } bytes and >= 0 ? (long)bytes : null;
}
