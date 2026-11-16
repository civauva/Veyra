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
