using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Query.Backups;
using Veyra.Query.Backups.Api;
using Veyra.Query.Metrics;
using Veyra.Query.Tests.Metrics;

namespace Veyra.Query.Tests.Backups;

/// <summary>
/// The Backups tab (ADR 0025): the backups of the window, their time per day, a 7-day grid, and each database's
/// freshness judged by the backup rules' thresholds.
/// </summary>
public class BackupsViewTests
{
    private const string _block = "microsoft.sql_server sql-prod-01";

    // Thursday 26 November 2026, 09:30 UTC; the window is the three days before it.
    private static readonly DateTimeOffset _to = new(2026, 11, 26, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _from = _to.AddDays(-3);

    private readonly SeriesBook _recent = new(MetricWindow.Covering(_to.AddMinutes(-30), _to, 30));

    [Fact]
    public void Build_Records_AreTheBackupsNewestFirstWithTheirTimeSizesAndDevice()
    {
        var log = Backup("9", "shop", "log", _to.AddMinutes(-20), 12, bytes: "73400320", compressed: "-1");
        var full = Backup("8", "shop", "full", _to.AddHours(-7), 1_800, copyOnly: "1") with { Body = "" };

        var view = Build([log, full], []);

        Assert.Equal(
            [("shop", "log"), ("shop", "full")],
            view.Backups.Select(backup => (backup.Database, backup.Type)));
        var first = view.Backups[0];
        Assert.Equal((_to.AddMinutes(-20).AddSeconds(-12), _to.AddMinutes(-20), 12d), (
            first.StartedAt,
            first.FinishedAt,
            first.Seconds));
        Assert.Equal((73400320L, (long?)null, false), (first.Bytes, first.CompressedBytes, first.CopyOnly));
        Assert.Equal("/backup/9.bak", first.Device);
        Assert.Equal((true, (string?)null), (view.Backups[1].CopyOnly, view.Backups[1].Device));
        Assert.Equal((_from, _to, false), (view.From, view.To, view.SelfManaged));
    }

    [Fact]
    public void Build_Days_AreEveryDayOfTheWindowWithTheTimeOfEachType()
    {
        BackupDay[] days =
        [
            new(Day(0), "shop", "log", 38, 380),
            new(Day(0), "reporting", "log", 2, 20),
            new(Day(-1), "shop", "full", 1, 1_800),
            new(Day(-1), "shop", "differential", 1, 240),
            new(Day(-3), "shop", "full", 1, 1_750),
        ];

        var view = Build([], days);

        Assert.Equal(
            ["2026-11-23", "2026-11-24", "2026-11-25", "2026-11-26"],
            view.Days.Select(day => day.Day));
        Assert.Equal((1, 1_750d), (view.Days[0].Full.Count, view.Days[0].Full.Seconds));
        Assert.Equal((0, 0, 0), (view.Days[1].Full.Count, view.Days[1].Differential.Count, view.Days[1].Log.Count));
        Assert.Equal((1, 240d), (view.Days[2].Differential.Count, view.Days[2].Differential.Seconds));
        Assert.Equal((40, 400d), (view.Days[3].Log.Count, view.Days[3].Log.Seconds));
    }

    [Fact]
    public void Build_Grid_IsTheLastSevenDaysOfEachDatabaseWithTheTypesThatRan()
    {
        _recent.Level("sqlserver.database.backup.age", 3_600, ("database_name", "archive"));
        BackupDay[] days =
        [
            new(Day(0), "shop", "log", 38, 380),
            new(Day(-1), "shop", "full", 1, 1_800),
            new(Day(-6), "shop", "differential", 1, 240),
            new(Day(-7), "shop", "full", 1, 1_800),
        ];

        var view = Build([], days);

        Assert.Equal(7, view.Grid.Days.Count);
        Assert.Equal(("2026-11-20", "2026-11-26"), (view.Grid.Days[0], view.Grid.Days[^1]));
        Assert.Equal(["archive", "shop"], view.Grid.Databases.Select(row => row.Database));
        var shop = view.Grid.Databases[1].Cells;
        Assert.Equal((false, true, 0), (shop[0].Full, shop[0].Differential, shop[0].Log));
        Assert.Equal((true, false, 0), (shop[5].Full, shop[5].Differential, shop[5].Log));
        Assert.Equal((false, false, 38), (shop[6].Full, shop[6].Differential, shop[6].Log));
        Assert.All(
            view.Grid.Databases[0].Cells,
            cell => Assert.Equal((false, false, 0), (cell.Full, cell.Differential, cell.Log)));
    }

    [Fact]
    public void Build_Freshness_IsJudgedByTheRulesThresholdsTheWorstFirst()
    {
        Database("shop", "FULL", full: 10 * 3600, log: 5 * 3600);
        Database("reporting", "SIMPLE", full: 30 * 3600, log: -1);
        Database("orders", "FULL", full: 2 * 3600, log: 600);
        Database("scratch", "SIMPLE", full: 90 * 86400, log: -1);
        _recent.Level("sqlserver.database.offline", 0, ("database_name", "fresh"), ("recovery_model", "FULL"));
        _recent.Level("sqlserver.database.log_backup.age", 7_200, ("database_name", "fresh"));
        _recent.Level("sqlserver.database.diff_backup.age", 4 * 3600, ("database_name", "orders"));
        var thresholds = BackupThresholds.Default with { IgnoredDatabases = ["Scratch"] };

        var view = Build([], [], thresholds);

        Assert.Equal(
            [
                ("fresh", "warning"),
                ("shop", "warning"),
                ("reporting", "caution"),
                ("orders", "ok"),
                ("scratch", "ignored"),
            ],
            view.Freshness.Select(database => (database.Database, database.State)));
        var shop = view.Freshness[1];
        Assert.Equal((10d * 3600, 5d * 3600, "FULL"), (shop.FullAgeSeconds, shop.LogAgeSeconds, shop.RecoveryModel));
        Assert.Equal(
            "Full backup within 26 h, warning after 50 h; log backup within 1 h, warning after 4 h",
            shop.Policy);
        Assert.Equal("Full backup within 26 h, warning after 50 h", view.Freshness[2].Policy);
        Assert.Null(view.Freshness[2].LogAgeSeconds);
        Assert.Equal(4d * 3600, view.Freshness[3].DiffAgeSeconds);
        Assert.Null(view.Freshness[0].FullAgeSeconds);
        Assert.Contains("on purpose", view.Freshness[4].Policy, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RulesSwitchedOff_JudgeNothing()
    {
        Database("shop", "FULL", full: 90 * 3600, log: 9 * 3600);
        var thresholds = BackupThresholds.Default with { FullJudged = false, LogJudged = false };

        var view = Build([], [], thresholds);

        var shop = Assert.Single(view.Freshness);
        Assert.Equal("ignored", shop.State);
        Assert.Equal("No backup rule is on", shop.Policy);
    }

    [Fact]
    public void Build_SystemDatabases_AreLeftOutUnlessAskedFor()
    {
        Database("shop", "FULL", full: 3600, log: 600);
        Database("msdb", "SIMPLE", full: 3600, log: -1);
        _recent.Level("sqlserver.database.offline", 0, ("database_name", "tempdb"), ("recovery_model", "SIMPLE"));
        ActivityRecord[] backups =
        [
            Backup("2", "msdb", "full", _to.AddHours(-1), 3),
            Backup("1", "shop", "log", _to.AddHours(-2), 3),
        ];
        BackupDay[] days = [new(Day(0), "msdb", "full", 1, 3), new(Day(0), "shop", "log", 1, 3)];

        var hidden = Build(backups, days);
        var shown = Build(backups, days, system: true);

        Assert.Equal(["shop"], hidden.Backups.Select(backup => backup.Database));
        Assert.Equal(["shop"], hidden.Freshness.Select(database => database.Database));
        Assert.Equal(["shop"], hidden.Grid.Databases.Select(row => row.Database));
        Assert.Equal(0, hidden.Days[^1].Full.Count);
        Assert.Equal(["msdb", "shop"], shown.Freshness.Select(database => database.Database).Order());
        Assert.Equal(1, shown.Days[^1].Full.Count);
    }

    [Theory]
    [InlineData("managed-instance", true)]
    [InlineData("azure-sql-database", true)]
    [InlineData("sqlserver", false)]
    public void Build_Platform_SaysWhetherItBacksUpByItself(string platform, bool selfManaged)
    {
        _recent.Level(
            "sqlserver.login.permission.missing",
            0,
            ("permission", "VIEW SERVER STATE"),
            ("login", "veyra_monitor"),
            ("platform", platform));

        Assert.Equal(selfManaged, Build([], []).SelfManaged);
    }

    private static DateOnly Day(int offset) => DateOnly.FromDateTime(_to.UtcDateTime).AddDays(offset);

    private void Database(string name, string recovery, double full, double log)
    {
        _recent.Level("sqlserver.database.offline", 0, ("database_name", name), ("recovery_model", recovery));
        _recent.Level("sqlserver.database.backup.age", full, ("database_name", name));
        _recent.Level("sqlserver.database.log_backup.age", log, ("database_name", name));
    }

    private BackupsResponse Build(
        IReadOnlyList<ActivityRecord> records,
        IReadOnlyList<BackupDay> days,
        BackupThresholds? thresholds = null,
        bool system = false) =>
        BackupsViewBuilder.Build(
            _from,
            _to,
            records,
            days,
            _recent.Data,
            thresholds ?? BackupThresholds.Default,
            system);

    private static ActivityRecord Backup(
        string key,
        string database,
        string type,
        DateTimeOffset finished,
        int seconds,
        string bytes = "1048576",
        string compressed = "524288",
        string copyOnly = "0") =>
        new(
            _block,
            "production",
            finished,
            "sqlserver.backup",
            key,
            new Dictionary<string, string>
            {
                ["database_name"] = database,
                ["backup_type"] = type,
                ["started"] = finished.AddSeconds(-seconds)
                    .ToUnixTimeMilliseconds()
                    .ToString(CultureInfo.InvariantCulture),
                ["bytes"] = bytes,
                ["compressed_bytes"] = compressed,
                ["copy_only"] = copyOnly,
            },
            "/backup/" + key + ".bak");
}
