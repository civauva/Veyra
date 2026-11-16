using Veyra.Core.Activity;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Live.Api;
using Veyra.Query.Metrics;

namespace Veyra.Query.Live;

/// <summary>
/// Builds a SQL Server's Live tab (ADR 0020, area 3) from its newest read of busy sessions, or the read at a chosen
/// moment, and two lines of metrics.
/// </summary>
/// <param name="activity">The reads of busy sessions.</param>
/// <param name="search">The metric series and points.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class LiveViewBuilder(IDatabaseActivitySearch activity, IMetricSearch search, TimeProvider clock)
{
    /// <summary>
    /// How old the newest read may be and still count as now. The agent reads every 10 seconds and its batch adds a
    /// little; two minutes rides out a slow network without showing a stale read as current for long.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan _lines = TimeSpan.FromMinutes(15);

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds the Live tab of a server.</summary>
    /// <param name="engine">The engine, such as <c>microsoft.sql_server</c>.</param>
    /// <param name="server">The server's name.</param>
    /// <param name="at">A moment to look back at, or <see langword="null"/> for now.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The tab, or <see langword="null"/> when the server sent neither reads nor metrics.</returns>
    public async Task<LiveResponse?> LiveAsync(
        string engine,
        string server,
        DateTimeOffset? at,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var moment = at is { } chosen && chosen < now ? chosen : now;
        var block = TelemetryBlock.Database(engine, server);
        var window = MetricWindow.Covering(moment - _lines, moment, 30);

        // The reads do not depend on each other, so they run at the same time (ADR 0015).
        var reading = activity.GetSessionsAsync(block.Name, moment, MaxAge, cancellationToken);
        var finding = search.FindSeriesAsync(
            new MetricSeriesQuery(
                window.Start,
                Names: [SqlServerNames.BatchRequests, SqlServerNames.Blocked],
                Block: block),
            cancellationToken);

        await Task.WhenAll(reading, finding);

        var snapshot = await reading;
        var series = await finding;

        if (snapshot is null && series.Count == 0)
        {
            return null;
        }

        var data = series.Count == 0
            ? SeriesData.Empty(window)
            : await _reader.ReadAsync(series, window, cancellationToken);
        var requests = data.Rates(data.Named(SqlServerNames.BatchRequests));
        var blocked = data.Levels(data.Named(SqlServerNames.Blocked));

        return Build(engine, server, snapshot, at is null ? null : moment, now, window, requests, blocked);
    }

    /// <summary>Turns a read into the tab. Pure; tested without a database.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server's name.</param>
    /// <param name="snapshot">The read, or <see langword="null"/> when there is none.</param>
    /// <param name="at">The moment asked for, or <see langword="null"/> for now.</param>
    /// <param name="now">The current time.</param>
    /// <param name="window">The window of the two lines.</param>
    /// <param name="requests">Batch requests per second per bucket.</param>
    /// <param name="blocked">Blocked sessions per bucket.</param>
    /// <returns>The tab.</returns>
    public static LiveResponse Build(
        string engine,
        string server,
        SessionSnapshot? snapshot,
        DateTimeOffset? at,
        DateTimeOffset now,
        MetricWindow window,
        IReadOnlyDictionary<DateTimeOffset, double> requests,
        IReadOnlyDictionary<DateTimeOffset, double> blocked)
    {
        ArgumentNullException.ThrowIfNull(window);

        var samples = snapshot?.Sessions ?? [];
        var trees = BlockingTrees.Build(samples);
        var counts = BlockingTrees.WaitingCounts(trees);
        var sessions = samples
            .Select(sample => Session(sample, counts.GetValueOrDefault(sample.SessionId)))
            .OrderByDescending(session => session.Status == "sleeping" ? 0 : 1)
            .ThenByDescending(session => session.Status == "sleeping"
                ? session.TransactionSeconds ?? 0
                : session.ElapsedSeconds)
            .ThenBy(session => session.Id)
            .ToList();
        var waits = sessions
            .Select(session => session.WaitType)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(type => type, SqlServerWaits.Explain, StringComparer.OrdinalIgnoreCase);
        var tempdb = snapshot?.Server;
        double? free = tempdb is { TempdbSizeKilobytes: > 0 }
            ? (double)tempdb.TempdbFreeKilobytes / tempdb.TempdbSizeKilobytes
            : null;
        var oldest = sessions
            .Where(session => session.TransactionSeconds is not null)
            .OrderByDescending(session => session.TransactionSeconds)
            .FirstOrDefault();
        var findings = snapshot is null
            ? [Missing(server)]
            : LiveFindings.Build(sessions, trees, free, tempdb?.GrantsWaiting ?? 0, waits);
        var requestLine = window.Line("Batch requests", requests);

        return new LiveResponse(
            engine,
            server,
            snapshot?.Environment,
            snapshot?.SampledAt,
            snapshot is null ? null : Math.Max(0, ((at ?? now) - snapshot.SampledAt).TotalSeconds),
            at,
            new LiveTilesResponse(
                BatchRequests: requestLine.Latest,
                BatchRequestsLine: requestLine,
                Blocked: sessions.Count(session => session.BlockedBy is not null),
                BlockedLine: window.Line("Blocked", blocked),
                LongestRequestSeconds: sessions
                    .Where(session => session.Status != "sleeping")
                    .Select(session => session.ElapsedSeconds)
                    .DefaultIfEmpty(0)
                    .Max(),
                OldestTransactionSeconds: oldest?.TransactionSeconds,
                OldestTransactionSession: oldest?.Id,
                GrantsWaiting: tempdb?.GrantsWaiting ?? 0,
                TempdbFreeRatio: free,
                TempdbSizeBytes: (tempdb?.TempdbSizeKilobytes ?? 0) * 1024),
            findings,
            trees,
            sessions,
            waits);
    }

    private static LiveSessionResponse Session(SessionSample sample, int waiting)
    {
        var running = sample.Running;

        return new LiveSessionResponse(
            Id: sample.SessionId,
            Status: running ? Status(sample.RequestStatus) : "sleeping",
            Command: sample.Command,
            Database: sample.Database,
            Program: sample.Program,
            Host: sample.Host,
            Login: sample.Login,
            BlockedBy: sample.BlockedBy > 0 ? sample.BlockedBy : null,
            Blocking: waiting,
            WaitType: string.IsNullOrEmpty(sample.WaitType) ? null : sample.WaitType,
            WaitSeconds: sample.WaitMilliseconds / 1000d,
            WaitResource: string.IsNullOrEmpty(sample.WaitResource) ? null : sample.WaitResource,
            ElapsedSeconds: running ? sample.ElapsedMilliseconds / 1000d : 0,
            CpuSeconds: sample.CpuMilliseconds / 1000d,
            Reads: sample.Reads,
            Writes: sample.Writes,
            TempdbBytes: sample.TempdbKilobytes * 1024,
            GrantBytes: sample.GrantKilobytes * 1024,
            GrantRequestedBytes: sample.GrantRequestedKilobytes * 1024,
            GrantWaiting: sample.GrantWaiting,
            TransactionSeconds: sample.TransactionMilliseconds / 1000d,
            ChangedData: sample.TransactionLogRecords > 0,
            OpenTransactions: sample.OpenTransactions,
            Isolation: Isolation(sample.Isolation),
            PercentComplete: sample.PercentComplete > 0 ? sample.PercentComplete : null,
            RemainingSeconds: sample.RemainingMilliseconds / 1000d,
            QueryHash: string.IsNullOrEmpty(sample.QueryHash) ? null : sample.QueryHash,
            PlanHash: string.IsNullOrEmpty(sample.PlanHash) ? null : sample.PlanHash,
            IdleSeconds: sample.IdleMilliseconds / 1000d,
            ConnectedSeconds: sample.ConnectedSeconds,
            Name: StatementNames.Describe(sample.Text),
            Text: sample.Text);
    }

    // SQL Server's request status; "background" and the like read as running.
    private static string Status(string status) => status switch
    {
        "suspended" or "runnable" or "running" => status,
        _ => "running",
    };

    private static string Isolation(int level) => level switch
    {
        1 => "read uncommitted",
        2 => "read committed",
        3 => "repeatable read",
        4 => "serializable",
        5 => "snapshot",
        _ => "unspecified",
    };

    private static FindingResponse Missing(string server) =>
        new(
            "no-read",
            "caution",
            $"Veyra has no live read of {server} from the last two minutes.",
            "The live view needs the agent that reads this server to run with its newest configuration, which Veyra "
            + "sends by itself. The server's state on Settings → Database monitoring says what is wrong.",
            "No sqlserver.session read within the last 2 minutes.",
            null,
            []);
}
