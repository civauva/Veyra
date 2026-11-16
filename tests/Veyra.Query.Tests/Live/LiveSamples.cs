using Veyra.Core.Activity;

namespace Veyra.Query.Tests.Live;

/// <summary>Reads of a SQL Server's live view for the tests, like the one of the ADR 0020 mockups.</summary>
internal static class LiveSamples
{
    /// <summary>The server's block.</summary>
    public const string Block = "microsoft.sql_server sql-prod-01";

    /// <summary>When the read was made.</summary>
    public static readonly DateTimeOffset At = new(2026, 11, 15, 11, 45, 19, TimeSpan.Zero);

    /// <summary>
    /// A head blocker that sleeps with an open transaction (88), one update waiting for it (117), and two reads
    /// waiting for the update (131, 140), plus a report running on its own (203).
    /// </summary>
    public static IReadOnlyList<SessionSample> Chain() =>
    [
        Idle(88) with
        {
            TransactionMilliseconds = 192_000,
            TransactionLogRecords = 2,
            OpenTransactions = 1,
            Blocking = true,
        },
        Waiting(117, 88, "LCK_M_U", 134_000) with
        {
            Command = "UPDATE",
            Blocking = true,
            Text = "UPDATE dbo.Orders SET Status = @status WHERE CustomerId = @customerId",
        },
        Waiting(131, 117, "LCK_M_S", 48_000) with { Program = "checkout-api" },
        Waiting(140, 117, "LCK_M_S", 31_000) with { Program = "checkout-api", Host = "web-22" },
        Running(203, 41_000) with { Program = "bi_reader", Database = "reporting" },
    ];

    /// <summary>A session without a request.</summary>
    public static SessionSample Idle(int id) =>
        new(
            Block,
            "production",
            At,
            SessionId: id,
            RequestStatus: "",
            SessionStatus: "sleeping",
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
            CpuMilliseconds: 10,
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
            IdleMilliseconds: 192_000,
            ConnectedSeconds: 7_440,
            Text: "BEGIN TRAN; UPDATE dbo.Orders SET Status = @status, ChangedAt = SYSUTCDATETIME() WHERE Id = @id",
            Running: false,
            Blocking: false);

    /// <summary>A session running a request that waits on nothing.</summary>
    public static SessionSample Running(int id, long elapsed) =>
        Idle(id) with
        {
            RequestStatus = "running",
            SessionStatus = "running",
            Command = "SELECT",
            ElapsedMilliseconds = elapsed,
            QueryHash = "0x091FB7C3F880F1BA",
            PlanHash = "0xC8B584140C628EDA",
            Text = "SELECT c.Region, SUM(o.Total) FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId",
            Running = true,
        };

    /// <summary>A session whose request waits for another.</summary>
    public static SessionSample Waiting(int id, int blocker, string wait, long waited) =>
        Running(id, waited + 100) with
        {
            RequestStatus = "suspended",
            BlockedBy = blocker,
            WaitType = wait,
            WaitMilliseconds = waited,
            WaitResource = "KEY: 5:72057594045726720 (8194443284a0)",
            Text = "SELECT o.Id, o.Total, o.Status FROM dbo.Orders AS o WHERE o.CustomerId = @customerId",
        };
}
