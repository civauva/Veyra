using System.Globalization;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Live;
using Veyra.Query.Live.Api;
using Veyra.Query.Metrics;
using Veyra.Query.Metrics.Api;
using Veyra.Query.Queries.Api;
using Veyra.Query.Server.Api;

namespace Veyra.Query.Server;

/// <summary>Builds a SQL Server's Server tab and its Waits explained (ADR 0020, areas 1, 2, and 14).</summary>
/// <param name="search">The metric series and points.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class ServerViewBuilder(IMetricSearch search, TimeProvider clock)
{
    private static readonly string[] _names =
    [
        "sqlserver.server.info",
        "sqlserver.cpu.sql.ratio",
        "sqlserver.cpu.other.ratio",
        "sqlserver.schedulers.runnable",
        "sqlserver.workers.ratio",
        "sqlserver.memory.target",
        "sqlserver.memory.committed",
        "sqlserver.os.memory.low",
        "sqlserver.os.memory.free",
        "sqlserver.memory.grants.pending",
        "sqlserver.memory.dumps.day",
        "sqlserver.agent.stopped",
        "sqlserver.server.unsupported",
        SqlServerNames.WaitTime,
        SqlServerNames.BatchRequests,
    ];

    private const int _maxWaits = 15;

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds the tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The range.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The tab, or <see langword="null"/> when the server sent nothing.</returns>
    public async Task<ServerResponse?> ServerAsync(
        string engine,
        string server,
        string? range,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var window = MetricWindow.Of(range, now);
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(
                window.Start,
                Names: _names,
                Limit: 5_000,
                Block: TelemetryBlock.Database(engine, server)),
            cancellationToken);

        if (series.Count == 0)
        {
            return null;
        }

        var requests = series.Where(one => one.Name == SqlServerNames.BatchRequests).ToList();
        var dataReading = _reader.ReadAsync(series, window, cancellationToken);
        var recentReading = _reader.ReadAsync(
            series,
            MetricWindow.Covering(now.AddMinutes(-15), now, 15),
            cancellationToken);
        var monthReading = _reader.ReadAsync(
            requests,
            MetricWindow.Covering(now.AddDays(-28), now, 28 * 24),
            cancellationToken);

        await Task.WhenAll(dataReading, recentReading, monthReading);

        return Build(await dataReading, await recentReading, await monthReading, now);
    }

    /// <summary>Turns the metrics into the tab. Pure; tested without a database.</summary>
    /// <param name="data">The range.</param>
    /// <param name="recent">The last 15 minutes.</param>
    /// <param name="month">Batch requests over the last four weeks, by hour.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The tab.</returns>
    public static ServerResponse Build(SeriesData data, SeriesData recent, SeriesData month, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(month);

        double? Now(string name) =>
            recent.Latest(recent.Levels(recent.Named(name))) is { } value && value >= 0 ? value : null;

        var info = recent.Named("sqlserver.server.info").MaxBy(one => one.LastSeen);
        var hourly = month.Rates(month.Named(SqlServerNames.BatchRequests));
        var same = hourly
            .Where(pair => pair.Key.DayOfWeek == now.DayOfWeek
                && pair.Key.Hour == now.Hour
                && now - pair.Key > TimeSpan.FromHours(1))
            .Select(pair => pair.Value)
            .ToList();
        var waits = data.Named(SqlServerNames.WaitTime)
            .GroupBy(one => MetricNames.Attribute(one, "wait.type") ?? "")
            .Where(group => group.Key.Length > 0)
            .Select(group =>
            {
                var rates = data.Rates(group);

                return (Type: group.Key, Seconds: rates.Values.Sum() * data.Window.Bucket.TotalSeconds);
            })
            .Where(wait => wait.Seconds > 0)
            .OrderByDescending(wait => wait.Seconds)
            .Take(_maxWaits)
            .Select(wait => new WaitTypeResponse(SqlServerWaits.Explain(wait.Type), wait.Seconds))
            .ToList();

        var response = new ServerResponse(
            info is null ? "" : MetricNames.Attribute(info, "version") ?? "",
            info is null ? "" : MetricNames.Attribute(info, "update_level") ?? "",
            info is null ? "" : MetricNames.Attribute(info, "edition") ?? "",
            Now("sqlserver.server.info"),
            Now("sqlserver.server.unsupported") >= 1,
            Now("sqlserver.cpu.sql.ratio"),
            Now("sqlserver.cpu.other.ratio"),
            Now("sqlserver.schedulers.runnable"),
            Now("sqlserver.workers.ratio"),
            Now("sqlserver.memory.target"),
            Now("sqlserver.memory.committed"),
            Now("sqlserver.os.memory.free"),
            Now("sqlserver.os.memory.low") >= 1,
            Now("sqlserver.memory.grants.pending"),
            Now("sqlserver.memory.dumps.day"),
            Now("sqlserver.agent.stopped") is { } stopped ? stopped >= 1 : null,
            recent.Latest(recent.Rates(recent.Named(SqlServerNames.BatchRequests))),
            same.Count == 0 ? null : same.Average(),
            data.Window.Line("SQL Server", data.Levels(data.Named("sqlserver.cpu.sql.ratio"))),
            data.Window.Line("Others", data.Levels(data.Named("sqlserver.cpu.other.ratio"))),
            waits,
            []);

        return response with { Findings = Findings(response) };
    }

    private static List<PlanWarningResponse> Findings(ServerResponse server)
    {
        var findings = new List<PlanWarningResponse>();

        if (server.Unsupported)
        {
            findings.Add(
                new(
                    "unsupported",
                    "warning",
                    $"SQL Server {server.Version} is out of support",
                    "Microsoft no longer fixes its security problems.",
                    "Plan an upgrade to a supported version.",
                    null));
        }

        if (server.AgentStopped == true)
        {
            findings.Add(
                new(
                    "agent-stopped",
                    "warning",
                    "SQL Server Agent is not running",
                    "No job runs: no backups, no index maintenance, no integrity checks.",
                    "Start the SQL Server Agent service and set it to start automatically.",
                    null));
        }

        if (server.OsMemoryLow)
        {
            findings.Add(
                new(
                    "os-memory-low",
                    "warning",
                    "The machine is low on memory",
                    "SQL Server will give memory back and read more from disk.",
                    "Lower other processes' memory, or set SQL Server's maximum server memory so the machine keeps "
                    + "some.",
                    null));
        }

        if (server.OtherCpu is >= 0.25)
        {
            findings.Add(
                new(
                    "cpu-other",
                    "caution",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Other processes take {server.OtherCpu * 100:0}% of the CPU"),
                    "SQL Server shares the machine with something busy, such as antivirus or another service.",
                    "Find the process and move it, or give the machine more CPUs.",
                    null));
        }

        if (server.SqlCpu is >= 0.8)
        {
            findings.Add(
                new(
                    "cpu",
                    server.SqlCpu >= 0.95 ? "warning" : "caution",
                    string.Create(CultureInfo.InvariantCulture, $"SQL Server uses {server.SqlCpu * 100:0}% of the CPU"),
                    "Requests wait for a processor and everything slows down.",
                    "The Queries tab ranked by CPU shows which statements use it.",
                    null));
        }

        if (server.Workers is >= 0.8)
        {
            findings.Add(
                new(
                    "workers",
                    "warning",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{server.Workers * 100:0}% of worker threads are in use"),
                    "When none is left, new connections and requests cannot start.",
                    "Usually a long blocking chain holds them: look at the Live tab.",
                    null));
        }

        if (server.Requests is { } requests && server.RequestsUsual is { } usual && usual > 1 && requests > usual * 2)
        {
            findings.Add(
                new(
                    "requests",
                    "caution",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{requests:0} requests a second, {requests / usual:0.#} times the usual for this hour"),
                    "The workload is far above the same hour of the last four weeks.",
                    "Check which application sends more than usual.",
                    null));
        }

        return findings;
    }
}
