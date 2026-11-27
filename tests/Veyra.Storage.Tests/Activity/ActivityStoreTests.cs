using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Activity;
using Veyra.Storage.Activity;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Activity;

/// <summary>
/// The live view of a database server is kept read by read (ADR 0020). These tests prove a read comes back as written,
/// that the read at a moment is the newest before it, that an empty read says nothing is busy, and that every server's
/// newest read can be listed for the rules.
/// </summary>
[Collection(PostgresGroup.Name)]
public class ActivityStoreTests(PostgresFixture postgres)
{
    private const string _server = "microsoft.sql_server sql-prod-01";

    private static readonly DateTimeOffset _now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsync_ThenGetSessionsAsync_ReturnsTheReadAsWritten()
    {
        await using var database = await CreateAsync();
        var store = new ActivityStore(database);
        var blocker = Session(88, _now) with
        {
            SessionStatus = "sleeping",
            TransactionMilliseconds = 192_000,
            TransactionLogRecords = 2,
            OpenTransactions = 1,
            Blocking = true,
            Text = "BEGIN TRAN; UPDATE dbo.Orders SET Status = @status WHERE Id = @id",
        };
        var waiter = Session(117, _now) with
        {
            RequestStatus = "suspended",
            Command = "UPDATE",
            BlockedBy = 88,
            WaitType = "LCK_M_U",
            WaitMilliseconds = 134_000,
            WaitResource = "KEY: 5:72057594045726720 (8194443284a0)",
            ElapsedMilliseconds = 134_200,
            QueryHash = "0x6ACBDEE9D5014909",
            PlanHash = "0x88EB92735AB6DB89",
            GrantKilobytes = 1024,
            GrantRequestedKilobytes = 2048,
            GrantWaiting = true,
            PercentComplete = 12.5,
            Running = true,
        };
        await store.WriteAsync([Server(_now, free: 61_440), blocker, waiter], default);

        var read = await store.GetSessionsAsync(_server, _now.AddSeconds(5), TimeSpan.FromMinutes(1), default);

        Assert.NotNull(read);
        Assert.Equal(_now, read.SampledAt);
        Assert.Equal(Server(_now, free: 61_440), read.Server);
        Assert.Equal([blocker, waiter], read.Sessions.OrderBy(session => session.SessionId));
    }

    [Fact]
    public async Task GetSessionsAsync_SeveralReads_ReturnsTheNewestAtOrBeforeTheMoment()
    {
        await using var database = await CreateAsync();
        var store = new ActivityStore(database);
        await store.WriteAsync([Server(_now.AddSeconds(-20)), Session(51, _now.AddSeconds(-20))], default);
        await store.WriteAsync([Server(_now.AddSeconds(-10)), Session(52, _now.AddSeconds(-10))], default);
        await store.WriteAsync([Server(_now), Session(53, _now)], default);

        var earlier = await store.GetSessionsAsync(_server, _now.AddSeconds(-5), TimeSpan.FromMinutes(1), default);

        Assert.Equal(52, Assert.Single(earlier!.Sessions).SessionId);
    }

    [Fact]
    public async Task GetSessionsAsync_NewestReadFoundNothingBusy_ReturnsItWithoutSessions()
    {
        await using var database = await CreateAsync();
        var store = new ActivityStore(database);
        await store.WriteAsync([Server(_now.AddSeconds(-10)), Session(51, _now.AddSeconds(-10))], default);
        await store.WriteAsync([Server(_now)], default);

        var read = await store.GetSessionsAsync(_server, _now, TimeSpan.FromMinutes(1), default);

        Assert.Equal(_now, read!.SampledAt);
        Assert.Empty(read.Sessions);
    }

    [Fact]
    public async Task GetSessionsAsync_NoReadWithinTheAgeOrAnotherServer_ReturnsNull()
    {
        await using var database = await CreateAsync();
        var store = new ActivityStore(database);
        await store.WriteAsync([Server(_now.AddMinutes(-5))], default);

        Assert.Null(await store.GetSessionsAsync(_server, _now, TimeSpan.FromMinutes(1), default));
        Assert.Null(await store.GetSessionsAsync("microsoft.sql_server other", _now, TimeSpan.FromHours(1), default));
    }

    [Fact]
    public async Task ListLatestSessionsAsync_SeveralServers_ReturnsTheNewestReadOfEach()
    {
        await using var database = await CreateAsync();
        var store = new ActivityStore(database);
        const string other = "microsoft.sql_server sql-prod-02";
        await store.WriteAsync([Server(_now.AddSeconds(-20)), Session(51, _now.AddSeconds(-20))], default);
        await store.WriteAsync([Server(_now.AddSeconds(-10)), Session(52, _now.AddSeconds(-10))], default);
        await store.WriteAsync([Server(_now.AddSeconds(-5)) with { Block = other }], default);
        await store.WriteAsync([Server(_now.AddMinutes(-10)) with { Block = "microsoft.sql_server gone" }], default);

        var reads = await store.ListLatestSessionsAsync(_now, TimeSpan.FromMinutes(2), default);

        Assert.Equal(2, reads.Count);
        Assert.Equal(52, Assert.Single(reads.Single(read => read.Block == _server).Sessions).SessionId);
        Assert.Empty(reads.Single(read => read.Block == other).Sessions);
    }

    [Fact]
    public async Task ApplyAsync_DaysOlderThanTheRetention_AreDropped()
    {
        await using var database = await CreateAsync();
        var store = new ActivityStore(database);
        var options = new TestOptionsMonitor<StorageOptions>(new StorageOptions { DatabaseSessionRetentionDays = 1 });
        await new ActivityPartitions(database, TimeProvider.System, NullLogger<ActivityPartitions>.Instance)
            .EnsureAsync(_now.AddDays(-3), default);
        await store.WriteAsync([Server(_now.AddDays(-3)), Server(_now)], default);

        var result = await new ActivityRetention(
                database,
                options,
                TimeProvider.System,
                NullLogger<ActivityRetention>.Instance)
            .ApplyAsync(_now, default);

        Assert.Contains("db_session_20261112", result.DroppedPartitions);
        Assert.Null(await store.GetSessionsAsync(_server, _now.AddDays(-3), TimeSpan.FromMinutes(1), default));
        Assert.NotNull(await store.GetSessionsAsync(_server, _now, TimeSpan.FromMinutes(1), default));
    }

    [Fact]
    public async Task ListRecordsMatchingAsync_MatchPastTheLimitOfNewerRecords_ReturnsIt()
    {
        await using var database = await CreateAsync();
        var store = new ActivityStore(database);
        var newer = Enumerable.Range(1, 5)
            .Select(index => Event($"ok-{index}", "connectivity_ring_buffer_recorded", _now.AddMinutes(-index)))
            .ToList<DatabaseActivity>();
        await store.WriteAsync(
            [
                .. newer,
                Event("damage", "error_reported", _now.AddHours(-1)),
                Event("stuck", "scheduler_non_yielding_rm", _now.AddHours(-2)),
            ],
            default);
        var match = new ActivityAttributeMatch("event_name", ["error_reported"], ["non_yielding"]);

        var records = await store.ListRecordsMatchingAsync(
            _server,
            "sqlserver.event",
            _now.AddDays(-1),
            _now,
            match,
            3,
            default);

        Assert.Equal(["damage", "stuck"], records.Select(record => record.Key));
    }

    [Fact]
    public async Task WriteAsync_SameBackupInThreeReads_IsKeptOnce()
    {
        await using var database = await CreateAsync();
        var store = new ActivityStore(database);
        var finished = _now.AddMinutes(-2);

        // The agent reads the last 15 minutes every 5 minutes, so a backup arrives in three reads, always with the time
        // it finished as its time.
        for (var read = 0; read < 3; read++)
        {
            await store.WriteAsync([Backup("48213", "shop", "log", finished, seconds: 4)], default);
        }

        var records = await store.ListRecordsAsync(
            _server,
            "sqlserver.backup",
            _now.AddDays(-1),
            _now,
            null,
            10,
            default);

        Assert.Equal(finished, Assert.Single(records).SampledAt);
    }

    [Fact]
    public async Task ListDaysAsync_Backups_AreCountedPerDayDatabaseAndTypeWithTheirTime()
    {
        await using var database = await CreateAsync();
        var store = new ActivityStore(database);
        await store.WriteAsync(
            [
                Backup("1", "shop", "full", _now.AddHours(-11), seconds: 1_800),
                Backup("2", "shop", "log", _now.AddHours(-2), seconds: 10),
                Backup("3", "shop", "log", _now.AddHours(-1), seconds: 20),
                Backup("4", "reporting", "differential", _now.AddHours(-1), seconds: 300),
                Backup("5", "shop", "log", _now.AddHours(-13), seconds: 30),
                Event("not-a-backup", "error_reported", _now.AddHours(-1)),
            ],
            default);

        var days = await store.ListDaysAsync(_server, _now.AddDays(-1), _now, default);

        var today = DateOnly.FromDateTime(_now.UtcDateTime);
        Assert.Equal(
            [
                new BackupDay(today, "reporting", "differential", 1, 300),
                new BackupDay(today, "shop", "full", 1, 1_800),
                new BackupDay(today, "shop", "log", 2, 30),
                new BackupDay(today.AddDays(-1), "shop", "log", 1, 30),
            ],
            days.OrderByDescending(day => day.Day).ThenBy(day => day.Database).ThenBy(day => day.Type));
    }

    [Fact]
    public async Task ListDaysCommand_LastWeek_ReadsOnlyThePartitionsOfThoseDaysByTheKindIndex()
    {
        await using var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var partitions = new ActivityPartitions(database, TimeProvider.System, NullLogger<ActivityPartitions>.Instance);

        for (var day = -30; day <= 0; day += 3)
        {
            await partitions.EnsureAsync(_now.AddDays(day), default);
        }

        await using var command = ActivityStore.ListDaysCommand(_server, _now.AddDays(-7), _now);

        var plan = await QueryPlan.ExplainAsync(database, command, [("enable_seqscan", "off")]);

        Assert.All(plan.Relations, relation => Assert.StartsWith("db_record_202611", relation));
        Assert.DoesNotContain("db_record_default", plan.Relations);
        Assert.InRange(plan.Relations.Distinct().Count(), 1, 9);
        Assert.All(plan.Indexes, index => Assert.Contains("kind", index, StringComparison.Ordinal));
        Assert.NotEmpty(plan.Indexes);
    }

    private static ActivityRecord Backup(
        string key,
        string database,
        string type,
        DateTimeOffset finished,
        int seconds) =>
        new(
            _server,
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
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["bytes"] = "1048576",
                ["compressed_bytes"] = "-1",
                ["copy_only"] = "0",
            },
            "/var/opt/mssql/backup/" + key + ".bak");

    private static ActivityRecord Event(string key, string name, DateTimeOffset at) =>
        new(
            _server,
            "production",
            at,
            "sqlserver.event",
            key,
            new Dictionary<string, string> { ["event_name"] = name },
            "");

    private static ServerSample Server(DateTimeOffset at, long free = 60_000) =>
        new(_server, "production", at, TempdbSizeKilobytes: 65_536, TempdbFreeKilobytes: free, GrantsWaiting: 0);

    private static SessionSample Session(int id, DateTimeOffset at) =>
        new(
            _server,
            "production",
            at,
            SessionId: id,
            RequestStatus: "",
            SessionStatus: "running",
            Command: "",
            Database: "shop",
            Program: "app_shop",
            Host: "web-21",
            Login: "svc_shop",
            BlockedBy: 0,
            WaitType: "",
            WaitMilliseconds: 0,
            WaitResource: "",
            ElapsedMilliseconds: 0,
            CpuMilliseconds: 12,
            Reads: 94,
            Writes: 5,
            TempdbKilobytes: 0,
            GrantKilobytes: 0,
            GrantRequestedKilobytes: 0,
            GrantWaiting: false,
            TransactionMilliseconds: null,
            TransactionLogRecords: 0,
            OpenTransactions: 0,
            Isolation: 2,
            PercentComplete: 0,
            RemainingMilliseconds: 0,
            QueryHash: "",
            PlanHash: "",
            IdleMilliseconds: 7_330,
            ConnectedSeconds: 7_440,
            Text: "SELECT 1",
            Running: false,
            Blocking: false);

    private async Task<NpgsqlDataSource> CreateAsync()
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        await new ActivityPartitions(database, TimeProvider.System, NullLogger<ActivityPartitions>.Instance)
            .EnsureAsync(_now, default);

        return database;
    }
}
