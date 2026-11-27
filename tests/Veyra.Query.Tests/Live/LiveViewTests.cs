using Veyra.Core.Activity;
using Veyra.Query.Live;
using Veyra.Query.Live.Api;
using Veyra.Query.Metrics;
using static Veyra.Query.Tests.Live.LiveSamples;

namespace Veyra.Query.Tests.Live;

/// <summary>
/// The Live tab of a SQL Server (ADR 0020, area 3). These tests prove its tiles, its sessions, and above all the
/// findings the Simple view leads with, written about programs and machines and with a statement to copy.
/// </summary>
public class LiveViewTests
{
    private static readonly MetricWindow _window = MetricWindow.Covering(At.AddMinutes(-15), At, 30);

    [Fact]
    public void Build_SleepingHeadBlocker_SaysWhoWaitsForWhomInWords()
    {
        var view = View(Chain());

        var finding = view.Findings[0];
        Assert.Equal("blocking", finding.Kind);
        Assert.Equal("warning", finding.Severity);
        Assert.Equal(
            "3 requests of app_shop and checkout-api are stuck behind one connection of app_shop on web-21.",
            finding.Title);
        Assert.Equal(
            "That connection started a change 3 minutes ago and never finished it, so it still holds the rows. The "
            + "others have waited up to 2 minutes. This usually means the application did not commit or roll back "
            + "after an error.",
            finding.Detail);
        Assert.StartsWith(
            "Session 88 holds an open transaction for 3:12 and runs nothing; 3 sessions wait",
            finding.Advanced);
        Assert.Equal(88, finding.Session);
        Assert.Contains(finding.Actions, action => action is { Kind: "copy", Value: "KILL 88;" });
        Assert.Contains(finding.Actions, action => action is { Kind: "logs", Value: "app_shop" });
    }

    [Fact]
    public void Build_Chain_FillsTheTilesSessionsAndWaitTerms()
    {
        var view = View(Chain());

        Assert.Equal(3, view.Tiles.Blocked);
        Assert.Equal(192, view.Tiles.OldestTransactionSeconds);
        Assert.Equal(88, view.Tiles.OldestTransactionSession);
        Assert.Equal(134.1, view.Tiles.LongestRequestSeconds);
        Assert.Equal(1 - (61_440d / 65_536), 1 - view.Tiles.TempdbFreeRatio!.Value, 6);
        Assert.Equal([117, 131, 203, 140, 88], view.Sessions.Select(session => session.Id));

        var head = view.Sessions.Single(session => session.Id == 88);
        Assert.Equal("sleeping", head.Status);
        Assert.Equal(3, head.Blocking);
        Assert.True(head.ChangedData);
        Assert.Equal("Transaction: update of Orders by Id", head.Name);
        Assert.Equal("read committed", head.Isolation);
        Assert.Equal("waits for a lock", view.Waits["LCK_M_U"].Simple);
        Assert.Equal(["LCK_M_S", "LCK_M_U"], view.Waits.Keys.Order());
    }

    [Fact]
    public void Build_NothingBusy_SaysSoWithOneCalmFinding()
    {
        var view = View([]);

        var finding = Assert.Single(view.Findings);
        Assert.Equal(("calm", "ok", "Nothing waits for anything."), (finding.Kind, finding.Severity, finding.Title));
        Assert.Empty(view.Blocking);
    }

    [Fact]
    public void Build_OldOpenTransactionLongRequestAndFullTempdb_AreEachFound()
    {
        var sessions = new[]
        {
            Idle(60) with { TransactionMilliseconds = 2_400_000, OpenTransactions = 1 },
            Running(61, 400_000) with { TempdbKilobytes = 6_000_000 },
        };
        var snapshot = new SessionSnapshot(
            Block,
            "production",
            At,
            new ServerSample(Block, "production", At, 10_000_000, 400_000, GrantsWaiting: 2),
            sessions);

        var findings = LiveViewBuilder
            .Build("microsoft.sql_server", "sql-prod-01", snapshot, null, At, _window, _empty, _empty)
            .Findings;

        Assert.Equal(
            ["open-transaction", "tempdb", "memory-grants", "long-request"],
            findings.Select(finding => finding.Kind));
        Assert.Equal(
            "A connection of app_shop on web-21 has had a transaction open for 40 minutes and runs nothing.",
            findings[0].Title);
        Assert.Equal("tempdb is 96% full.", findings[1].Title);
        Assert.Equal("2 requests wait for memory to run.", findings[2].Title);
        Assert.Equal("Read from Orders of app_shop has run for 6 minutes.", findings[3].Title);
    }

    [Fact]
    public void Build_NoRead_SaysTheServerSentNoneAndHowOldNothingIs()
    {
        var view = LiveViewBuilder.Build(
            "microsoft.sql_server",
            "sql-prod-01",
            null,
            null,
            At,
            _window,
            _empty,
            _empty);

        Assert.Null(view.SampledAt);
        Assert.Equal("no-read", Assert.Single(view.Findings).Kind);
    }

    [Fact]
    public void Build_FilteredByProgram_ListsItsSessionsAndKeepsTheirChainsWhole()
    {
        var view = View(Chain(), new LiveFilter(Program: "checkout-api"));

        Assert.Equal([131, 140], view.Sessions.Select(session => session.Id));
        Assert.Equal(88, Assert.Single(view.Blocking).Session);
        // The chain's other sessions come apart, so the chain can still name who blocks whom.
        Assert.Equal([117, 88], view.Chained.Select(session => session.Id));
        // The tiles and findings stay the server's: a filter narrows the list, not what the server suffers.
        Assert.Equal(3, view.Tiles.Blocked);
        Assert.Equal(117, view.Tiles.LongestRequestSession);
        Assert.Equal("blocking", view.Findings[0].Kind);
    }

    [Fact]
    public void Build_FilteredByDatabaseAndWait_KeepsOnlySessionsThatMeetBoth()
    {
        var reporting = View(Chain(), new LiveFilter(Database: "reporting"));
        var waiting = View(
            Chain(),
            new LiveFilter(Database: "shop", Wait: "LCK_M_U", Host: "web-21", Login: "svc_shop"));

        Assert.Equal([203], reporting.Sessions.Select(session => session.Id));
        Assert.Empty(reporting.Blocking);
        Assert.Empty(reporting.Chained);
        Assert.Equal([117], waiting.Sessions.Select(session => session.Id));
        Assert.Equal([88, 131, 140], waiting.Chained.Select(session => session.Id).Order());
    }

    private static readonly Dictionary<DateTimeOffset, double> _empty = [];

    private static LiveResponse View(IReadOnlyList<SessionSample> sessions, LiveFilter? filter = null) =>
        LiveViewBuilder.Build(
            "microsoft.sql_server",
            "sql-prod-01",
            new SessionSnapshot(
                Block,
                "production",
                At,
                new ServerSample(Block, "production", At, 65_536, 61_440, 0),
                sessions),
            null,
            At.AddSeconds(7),
            _window,
            _empty,
            _empty,
            filter);
}
