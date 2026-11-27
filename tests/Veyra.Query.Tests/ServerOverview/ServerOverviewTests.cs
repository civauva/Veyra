using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Query.Metrics;
using Veyra.Query.ServerOverview;
using Veyra.Query.ServerOverview.Api;
using Veyra.Query.Tests.Live;
using Veyra.Query.Tests.Metrics;

namespace Veyra.Query.Tests.ServerOverview;

/// <summary>
/// A SQL Server's Overview in one read (ADR 0025): each part built from the reads of one moment, with the figures
/// compared with the equally long period before.
/// </summary>
public class ServerOverviewTests
{
    // 40 seconds after the live read of the samples, so it counts as now.
    private static readonly DateTimeOffset _now = LiveSamples.At.AddSeconds(41);

    private static readonly MetricWindow _window = MetricWindow.Of("1h", _now);

    private readonly SeriesBook _lines = new(_window.AndBefore());
    private readonly SeriesBook _recent = new(MetricWindow.Covering(_now.AddMinutes(-30), _now, 30));
    private readonly SeriesBook _day = new(MetricWindow.Day(_now));
    private readonly SeriesBook _month = new(MetricWindow.Covering(_now.AddDays(-30), _now, 30));
    private readonly SeriesBook _week = new(MetricWindow.Covering(_now.AddDays(-7), _now, 7));

    [Fact]
    public void Build_Figures_HaveTheirValueNowLineAndChangeAgainstThePeriodBefore()
    {
        _lines.Levels("sqlserver.cpu.sql.ratio", start => Before(start) ? 0.2 : 0.35);
        _lines.Total("sqlserver.batch.request.rate", start => Before(start) ? 100 : 150);
        _lines.Levels("sqlserver.user.connection.count", start => Before(start) ? null : 40);
        _recent.Level("sqlserver.cpu.sql.ratio", 0.4);
        _recent.Total("sqlserver.batch.request.rate", _ => 160);

        var figures = Build().Figures;

        Assert.Equal(0.4, figures.Cpu.Value);
        Assert.Equal(0.15, figures.Cpu.Change!.Value, 6);
        Assert.Equal(_window.Starts.Count, figures.Cpu.Line.Count);
        Assert.All(figures.Cpu.Line, value => Assert.Equal(0.35, value));
        Assert.Equal(160, figures.BatchRequests.Value!.Value, 6);
        Assert.Equal(0.5, figures.BatchRequests.Change!.Value, 6);
        Assert.Null(figures.Sessions.Change);
        Assert.Empty(figures.LastFullBackupSeconds.Line);
    }

    [Fact]
    public void Build_Memory_IsBytesAndAShareOfTheMachines()
    {
        _lines.Level("sqlserver.memory.usage", 8 * 1024 * 1024);
        _lines.Level("sqlserver.os.memory.total", 32d * 1024 * 1024 * 1024);
        _recent.Level("sqlserver.memory.usage", 8 * 1024 * 1024);

        var view = Build();

        Assert.Equal(8d * 1024 * 1024 * 1024, view.Figures.SqlMemory.Value);
        Assert.All(view.Performance.SqlMemoryRatio, ratio => Assert.Equal(0.25, ratio));
        Assert.Equal(_window.Starts, view.Performance.Starts);
        Assert.Equal(60, view.Performance.BucketSeconds);
    }

    [Fact]
    public void Build_Machine_NamesTheInstanceItsOperatingSystemAndWhenItStarted()
    {
        _recent.Level(
            "sqlserver.server.info",
            86_400,
            ("version", "16.0.4135.4"),
            ("update_level", "CU14"),
            ("edition", "Enterprise Edition (64-bit)"),
            ("os_name", "Windows Server 2022 Datacenter"),
            ("os_release", ""));
        _recent.Level("sqlserver.cpu.count", 16);
        _recent.Level("sqlserver.os.memory.total", 64d * 1024 * 1024 * 1024);
        _recent.Level("sqlserver.database.size", 1e9, ("database_name", "shop"), ("file_type", "data"));
        _recent.Level("sqlserver.database.size", 1e9, ("database_name", "msdb"), ("file_type", "data"));
        _recent.Level(
            "sqlserver.login.permission.missing",
            0,
            ("permission", "VIEW SERVER STATE"),
            ("login", "veyra_monitor"),
            ("platform", "sqlserver"));

        var machine = Build().Machine;

        Assert.Equal(("16.0.4135.4", "CU14", "Enterprise Edition (64-bit)"), (
            machine.Version,
            machine.UpdateLevel,
            machine.Edition));
        Assert.Equal(("Windows Server 2022 Datacenter", (string?)null), (machine.OsName, machine.OsRelease));
        Assert.Equal((16, 64L * 1024 * 1024 * 1024, 86_400L), (
            machine.Cpus,
            machine.MemoryBytes,
            machine.UptimeSeconds));

        // The last point of the read is in the middle of its newest minute; the instance started a day before it.
        var lastPoint = _recent.Starts[^1].AddSeconds(30);
        Assert.Equal(lastPoint.AddDays(-1), machine.StartedAt);
        Assert.Equal((1, "sqlserver"), (machine.Databases, machine.Platform));
    }

    [Fact]
    public void Build_WaitsAndCounters_LeaveIdleWaitsOutAndKeepTheTopFiveAndTheRest()
    {
        string[] types =
        [
            "PAGEIOLATCH_SH", "LCK_M_U", "WRITELOG", "CXPACKET", "SOS_SCHEDULER_YIELD", "ASYNC_NETWORK_IO",
        ];

        for (var index = 0; index < types.Length; index++)
        {
            var perSecond = 0.6 - (index * 0.1);
            _lines.Total(
                "sqlserver.os.wait.duration",
                start => Before(start) ? perSecond / 2 : perSecond,
                ("wait.category", "Category " + index),
                ("wait.type", types[index]));
        }

        _lines.Total("sqlserver.os.wait.duration", _ => 50, ("wait.category", "Idle"), ("wait.type", "SLEEP_TASK"));
        _lines.Total("sqlserver.batch.sql_compilation.rate", _ => 12);
        _lines.Level("sqlserver.page.life_expectancy", 3_400);

        var view = Build();

        var seconds = _window.Starts.Count * 60d;
        Assert.Equal(2.1 * seconds, view.Waits.TotalSeconds, 6);
        Assert.Equal(
            ["PAGEIOLATCH_SH", "LCK_M_U", "WRITELOG", "CXPACKET", "SOS_SCHEDULER_YIELD", "Other"],
            view.Waits.Types.Select(wait => wait.Type));
        Assert.Equal("Category 0", view.Waits.Types[0].Category);
        Assert.Equal(0.6 / 2.1, view.Waits.Types[0].Share, 6);
        Assert.Null(view.Waits.Types[^1].Category);
        Assert.Equal(0.1 * seconds, view.Waits.Types[^1].Seconds, 6);

        var counters = view.Counters.ToDictionary(counter => counter.Name);
        Assert.Equal(
            ["batchRequests", "compilations", "pageLife", "waits"],
            view.Counters.Select(counter => counter.Name));
        Assert.Equal(2_100, counters["waits"].Value!.Value, 6);
        Assert.Equal(1, counters["waits"].Change!.Value, 6);
        Assert.Equal(12, counters["compilations"].Value!.Value, 6);
        Assert.Equal("per second", counters["compilations"].Unit);
        Assert.Equal((3_400d, 0d), (counters["pageLife"].Value, counters["pageLife"].Change));
        Assert.All(view.Performance.WaitMsPerSecond, value => Assert.Equal(2_100, value!.Value, 6));
    }

    [Fact]
    public void Build_Io_IsBytesPerSecondByDirection()
    {
        _lines.Total("sqlserver.database.io", _ => 4_000, ("direction", "read"), ("logical_filename", "shop"));
        _lines.Total("sqlserver.database.io", _ => 1_000, ("direction", "read"), ("logical_filename", "shop_log"));
        _lines.Total("sqlserver.database.io", _ => 2_500, ("direction", "write"), ("logical_filename", "shop_log"));

        var io = Build().Io;

        Assert.Equal(5_000, io.ReadBytesPerSecond!.Value, 6);
        Assert.Equal(2_500, io.WriteBytesPerSecond!.Value, 6);
        Assert.Equal(_window.Starts.Count, io.ReadLine.Count);
    }

    [Fact]
    public void Build_Storage_HasTheTotalItsGrowthTheLargestAndTheVolumes()
    {
        var newest = _month.Starts[^1];
        _month.Levels(
            "sqlserver.database.size",
            start => 100e9 + ((start - newest).TotalDays * 1e9),
            ("database_name", "shop"),
            ("file_type", "data"));
        _day.Levels(
            "sqlserver.database.size",
            start => 100e9 + ((start - _day.Starts[^1]).TotalHours * 1e9 / 24),
            ("database_name", "shop"),
            ("file_type", "data"));
        _recent.Level("sqlserver.database.size", 100e9, ("database_name", "shop"), ("file_type", "data"));
        _recent.Level("sqlserver.database.size", 2e9, ("database_name", "shop"), ("file_type", "log"));
        _recent.Level("sqlserver.database.size", 1e9, ("database_name", "msdb"), ("file_type", "data"));
        _recent.Level("sqlserver.volume.size", 1e12, ("volume", "D:\\"));
        _recent.Level("sqlserver.volume.free", 1e11, ("volume", "D:\\"));
        _recent.Level("sqlserver.volume.size", 1e12, ("volume", "E:\\"));
        _recent.Level("sqlserver.volume.free", 5e11, ("volume", "E:\\"));
        _week.Levels(
            "sqlserver.volume.free",
            start => 1e11 - ((start - _week.Starts[^1]).TotalDays * 1e10),
            ("volume", "D:\\"));

        var view = Build();
        var storage = view.Storage;

        Assert.Equal(103_000_000_000L, storage.TotalBytes);
        Assert.Equal(1_000_000_000L, storage.Growth24hBytes);
        Assert.Equal((7_000_000_000L, 30_000_000_000L), (storage.Growth7dBytes, storage.Growth30dBytes));
        Assert.Equal(_month.Starts.Count - 1, storage.Line.Count);
        Assert.Equal(100_000_000_000L, storage.Line[^1]);
        var shop = Assert.Single(storage.Largest);
        Assert.Equal(("shop", 102_000_000_000L, 7_000_000_000L), (shop.Database, shop.Bytes, shop.Growth7dBytes));
        Assert.Equal(["D:\\", "E:\\"], storage.Volumes.Select(volume => volume.Name));
        Assert.Equal((0.1, 10d), (storage.Volumes[0].FreeRatio, Math.Round(storage.Volumes[0].DaysUntilFull!.Value)));
        Assert.Equal(("D:\\", 0.1), (view.Figures.VolumeFree.Volume, view.Figures.VolumeFree.Value));
    }

    [Fact]
    public void Build_Inventory_HasEachDatabaseWithItsSizesStateAndRecoveryModel()
    {
        _recent.Level("sqlserver.database.size", 40e9, ("database_name", "shop"), ("file_type", "data"));
        _recent.Level("sqlserver.database.size", 4e9, ("database_name", "shop"), ("file_type", "log"));
        _recent.Level("sqlserver.database.size", 1e9, ("database_name", "master"), ("file_type", "data"));
        _recent.Level(
            "sqlserver.database.offline",
            0,
            ("database_name", "shop"),
            ("state", "ONLINE"),
            ("recovery_model", "FULL"));
        _recent.Level(
            "sqlserver.database.offline",
            1,
            ("database_name", "archive"),
            ("state", "OFFLINE"),
            ("recovery_model", "SIMPLE"));
        _day.Levels(
            "sqlserver.database.size",
            start => 40e9 + ((start - _day.Starts[0]).TotalHours * 1e8),
            ("database_name", "shop"),
            ("file_type", "data"));

        var hidden = Build().Inventory;
        var shown = Build(system: true).Inventory;

        Assert.Equal(["shop", "archive"], hidden.Select(database => database.Database));
        var shop = hidden[0];
        Assert.Equal((40_000_000_000L, 4_000_000_000L), (shop.DataBytes, shop.LogBytes));
        Assert.Equal(("ONLINE", "FULL", 2_400_000_000L), (shop.State, shop.RecoveryModel, shop.GrowthPerDayBytes));
        Assert.Equal(("OFFLINE", (long?)null), (hidden[1].State, hidden[1].DataBytes));
        Assert.Contains("master", shown.Select(database => database.Database));
    }

    [Fact]
    public void Build_LastFullBackup_IsTheOldestOfTheUserDatabasesTheRuleJudges()
    {
        _recent.Level("sqlserver.database.backup.age", 30 * 3600, ("database_name", "shop"));
        _recent.Level("sqlserver.database.backup.age", 90 * 3600, ("database_name", "scratch"));
        _recent.Level("sqlserver.database.backup.age", 99 * 3600, ("database_name", "master"));

        var view = Build(thresholds: BackupThresholds.Default with { IgnoredDatabases = ["scratch"] });

        Assert.Equal(30d * 3600, view.Figures.LastFullBackupSeconds.Value);
        Assert.Null(view.Figures.LastFullBackupSeconds.Change);
    }

    [Fact]
    public void Build_Blocking_HasTheChainsOfTheLatestReadTheBlockedLineAndTheDeadlocks()
    {
        _lines.Level("sqlserver.processes.blocked", 3);
        _lines.Total("sqlserver.deadlock.rate", start => Before(start) ? 0 : 1 / 60d);

        var blocking = Build(snapshot: Snapshot(LiveSamples.At)).Blocking;

        var chain = Assert.Single(blocking.Chains);
        Assert.Equal((88, 3, "shop", "svc_shop", "web-21"), (
            chain.HeadSessionId,
            chain.BlockedCount,
            chain.Database,
            chain.Login,
            chain.Host));
        Assert.Null(chain.WaitType);
        Assert.StartsWith("BEGIN TRAN;", chain.Statement, StringComparison.Ordinal);
        Assert.Equal([117, 131, 140], chain.Blocked.Select(session => session.SessionId));
        Assert.Equal(("LCK_M_U", 134d), (chain.Blocked[0].WaitType, chain.Blocked[0].WaitSeconds));
        Assert.All(blocking.BlockedLine, value => Assert.Equal(3, value));
        Assert.Equal(_window.Starts.Count, blocking.Deadlocks);
    }

    [Fact]
    public void Build_StaleLiveRead_HasNoChains()
    {
        Assert.Empty(Build(snapshot: Snapshot(_now.AddMinutes(-10))).Blocking.Chains);
    }

    [Fact]
    public void Build_Coverage_SaysWhichAreasDeliverAndThePermissionAQuietOneLacks()
    {
        _recent.Level(
            "sqlserver.login.permission.missing",
            1,
            ("permission", "SELECT ON msdb.dbo.sysjobs"),
            ("login", "veyra_monitor"),
            ("platform", "sqlserver"));
        _recent.Level(
            "sqlserver.login.permission.missing",
            0,
            ("permission", "VIEW SERVER STATE"),
            ("login", "veyra_monitor"),
            ("platform", "sqlserver"));
        MetricSeries[] found =
        [
            Seen("sqlserver.os.wait.duration", _now.AddMinutes(-1)),
            Seen("sqlserver.database.backup.age", _now.AddHours(-2)),
        ];

        var coverage = Build(
                snapshot: Snapshot(LiveSamples.At),
                series: found,
                configurationSeen: _now.AddHours(-20))
            .Coverage
            .ToDictionary(area => area.Area);

        Assert.Equal(
            ["activity", "waits", "queries", "storage", "backups", "jobs", "events", "configuration", "availability"],
            coverage.Keys);
        Assert.True(coverage["activity"].Delivering);
        Assert.Equal(LiveSamples.At, coverage["activity"].LastSeen);
        Assert.True(coverage["waits"].Delivering);
        Assert.True(coverage["configuration"].Delivering);
        Assert.False(coverage["backups"].Delivering);
        Assert.Equal(_now.AddHours(-2), coverage["backups"].LastSeen);
        Assert.Null(coverage["backups"].MissingPermission);
        Assert.Equal((false, (DateTimeOffset?)null), (coverage["jobs"].Delivering, coverage["jobs"].LastSeen));
        Assert.Equal("SELECT ON msdb.dbo.sysjobs", coverage["jobs"].MissingPermission);
        Assert.Null(coverage["waits"].MissingPermission);
    }

    private static bool Before(DateTimeOffset start) => start < _window.Start;

    private static SessionSnapshot Snapshot(DateTimeOffset at) =>
        new(
            LiveSamples.Block,
            "production",
            at,
            new ServerSample(LiveSamples.Block, "production", at, 65_536, 61_440, 0),
            [.. LiveSamples.Chain().Select(session => session with { SampledAt = at })]);

    private static MetricSeries Seen(string name, DateTimeOffset at) =>
        new(
            1,
            name,
            MetricKind.Gauge,
            "1",
            "",
            MetricTemporality.Unspecified,
            false,
            null,
            "production",
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            at.AddDays(-1),
            at,
            SeriesBook.Server);

    private ServerOverviewResponse Build(
        bool system = false,
        BackupThresholds? thresholds = null,
        SessionSnapshot? snapshot = null,
        IReadOnlyList<MetricSeries>? series = null,
        DateTimeOffset? configurationSeen = null) =>
        ServerOverviewBuilder.Build(
            new ServerOverviewReads(
                _window,
                _lines.Data,
                _recent.Data,
                _day.Data,
                _month.Data,
                _week.Data,
                series ?? [],
                snapshot,
                null,
                configurationSeen),
            thresholds ?? BackupThresholds.Default,
            system);
}
