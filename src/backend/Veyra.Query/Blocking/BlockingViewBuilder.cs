using System.Xml;
using System.Xml.Linq;
using Veyra.Core.Activity;
using Veyra.Core.Telemetry;
using Veyra.Query.Blocking.Api;
using Veyra.Query.Live;
using Veyra.Query.Metrics;

namespace Veyra.Query.Blocking;

/// <summary>
/// Builds a SQL Server's Blocking tab (ADR 0020, area 5): blocking episodes from the live view's reads, and the
/// deadlocks of the built-in <c>system_health</c> session with their graphs.
/// </summary>
/// <param name="activity">The live view's reads and the deadlock records.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class BlockingViewBuilder(IDatabaseActivitySearch activity, TimeProvider clock)
{
    /// <summary>The kind of record a deadlock is.</summary>
    public const string DeadlockKind = "sqlserver.deadlock";

    private const int _maxRows = 50_000;
    private const int _maxDeadlocks = 200;

    // Reads further apart than this belong to different episodes: the agent reads every 10 seconds.
    private static readonly TimeSpan _gap = TimeSpan.FromSeconds(35);

    /// <summary>Builds the tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The tab.</returns>
    public async Task<BlockingResponse> BlockingAsync(
        string engine,
        string server,
        MetricSpan span,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(span, clock.GetUtcNow());
        var block = TelemetryBlock.Database(engine, server).Name;
        var rowsReading = activity.ListBlockingAsync(block, window.Start, window.End, _maxRows, cancellationToken);
        var deadlocksReading = activity.ListRecordsAsync(
            block,
            DeadlockKind,
            window.Start,
            window.End,
            null,
            _maxDeadlocks,
            cancellationToken);

        await Task.WhenAll(rowsReading, deadlocksReading);

        return new BlockingResponse(
            window.Range,
            Episodes(await rowsReading),
            (await deadlocksReading).Select(record => Deadlock(record.Key, record.SampledAt, record.Body)).ToList())
        {
            From = window.From,
            To = window.End,
        };
    }

    /// <summary>
    /// Joins the reads of blocking sessions into episodes: the same head blocker in reads no more than 35 seconds
    /// apart.
    /// Pure; tested without a database.
    /// </summary>
    /// <param name="rows">The sessions that blocked or waited, oldest read first.</param>
    /// <returns>The episodes, the newest first.</returns>
    public static IReadOnlyList<EpisodeResponse> Episodes(IReadOnlyList<SessionSample> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var open = new Dictionary<
            (int Head, string Program),
            List<(SessionSample Head, List<SessionSample> Waiting)>>();
        var finished = new List<List<(SessionSample Head, List<SessionSample> Waiting)>>();

        foreach (var read in rows.GroupBy(row => row.SampledAt).OrderBy(group => group.Key))
        {
            var sessions = read.ToList();

            foreach (var tree in BlockingTrees.Build(sessions))
            {
                var head = sessions.FirstOrDefault(session => session.SessionId == tree.Session);

                if (head is null)
                {
                    continue;
                }

                var waiting = sessions.Where(session => session.BlockedBy > 0 && Behind(session, tree)).ToList();
                var key = (tree.Session, head.Program);

                if (open.TryGetValue(key, out var episode) && read.Key - episode[^1].Head.SampledAt > _gap)
                {
                    finished.Add(episode);
                    open.Remove(key);
                }

                if (!open.TryGetValue(key, out episode))
                {
                    open[key] = episode = [];
                }

                episode.Add((head, waiting));
            }
        }

        return finished.Concat(open.Values)
            .Select(Episode)
            .OrderByDescending(episode => episode.Start)
            .ToList();
    }

    /// <summary>Reads a deadlock graph. Pure; tested without a database.</summary>
    /// <param name="key">What identifies it.</param>
    /// <param name="at">When it happened.</param>
    /// <param name="xml">The graph, as <c>system_health</c> keeps it.</param>
    /// <returns>The deadlock; empty when the graph is not XML.</returns>
    public static DeadlockResponse Deadlock(string key, DateTimeOffset at, string xml)
    {
        XElement root;

        try
        {
            root = XElement.Parse(xml);
        }
        catch (XmlException)
        {
            return new DeadlockResponse(key, at, "A deadlock whose graph Veyra could not read.", [], []);
        }

        var victims = root
            .Descendants("victimProcess")
            .Select(victim => (string?)victim.Attribute("id") ?? "")
            .ToHashSet();
        var processes = root.Descendants("process")
            .Select(process =>
            {
                var text = process.Element("inputbuf")?.Value.Trim() ?? "";
                var id = (string?)process.Attribute("id") ?? "";

                return new DeadlockProcessResponse(
                    id,
                    (int?)process.Attribute("spid") ?? 0,
                    (string?)process.Attribute("clientapp") ?? "",
                    (string?)process.Attribute("hostname") ?? "",
                    (string?)process.Attribute("loginname") ?? "",
                    (string?)process.Attribute("currentdbname") ?? "",
                    victims.Contains(id),
                    $"{(string?)process.Attribute("lockMode")} on {(string?)process.Attribute("waitresource")}",
                    StatementNames.Describe(text),
                    text);
            })
            .ToList();
        var resources = (root.Element("resource-list")?.Elements() ?? [])
            .Select(resource => new DeadlockResourceResponse(
                resource.Name.LocalName.Replace("lock", "", StringComparison.Ordinal),
                (string?)resource.Attribute("objectname") is { } target
                    ? target + ((string?)resource.Attribute("indexname") is { } index ? $" · {index}" : "")
                    : (string?)resource.Attribute("id") ?? "",
                resource.Descendants("owner")
                    .Select(owner => $"{(string?)owner.Attribute("id")} ({(string?)owner.Attribute("mode")})")
                    .ToList(),
                resource.Descendants("waiter")
                    .Select(waiter => $"{(string?)waiter.Attribute("id")} ({(string?)waiter.Attribute("mode")})")
                    .ToList()))
            .ToList();
        var victim = processes.FirstOrDefault(process => process.Victim);
        var others = processes
            .Where(process => !process.Victim)
            .Select(process => Words.Program(process.Program))
            .Distinct()
            .ToList();
        var target = resources.FirstOrDefault()?.Target;
        var summary = victim is null
            ? $"{Words.Count(processes.Count, "connection")} deadlocked."
            : $"{Capital(Words.List(others.Count == 0 ? ["another connection"] : others))} and "
            + $"{Words.Program(victim.Program)} wanted each other's locks"
            + (target is { Length: > 0 } ? $" on {target}" : "")
            + $"; SQL Server ended {Words.Program(victim.Program)}'s {victim.Name.ToLowerInvariant()}.";

        return new DeadlockResponse(key, at, summary, processes, resources);
    }

    private static bool Behind(SessionSample session, Live.Api.BlockingNodeResponse tree) =>
        tree.Children.Any(child => child.Session == session.SessionId || Behind(session, child));

    private static EpisodeResponse Episode(List<(SessionSample Head, List<SessionSample> Waiting)> reads)
    {
        var first = reads[0].Head;
        var last = reads[^1].Head;
        var waiting = reads.SelectMany(read => read.Waiting).ToList();
        var programs = waiting.Select(session => Words.Program(session.Program)).Distinct().Order().ToList();
        var longest = waiting.Count == 0 ? 0 : waiting.Max(session => session.WaitMilliseconds) / 1000d;
        var most = reads.Max(read => read.Waiting.Count);
        var sleeping = reads.Any(read => !read.Head.Running);
        var place = string.IsNullOrWhiteSpace(first.Host) ? "" : $" on {first.Host}";
        var lasted = Words.Duration((last.SampledAt - first.SampledAt).TotalSeconds + 10);

        return new EpisodeResponse(
            first.SampledAt,
            last.SampledAt,
            first.SessionId,
            first.Program,
            first.Host,
            sleeping,
            StatementNames.Describe(first.Text),
            first.Text,
            most,
            longest,
            programs,
            $"For about {lasted}, {Words.Count(most, "request")} of {Words.List(programs)} waited behind "
            + $"{(sleeping ? "a connection that held a transaction open" : "a request")} of "
            + $"{Words.Program(first.Program)}{place}, up to {Words.Duration(longest)}.");
    }

    private static string Capital(string words) =>
        words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
}
