using System.Globalization;
using Veyra.Query.Live.Api;

namespace Veyra.Query.Live;

/// <summary>
/// What Veyra makes of one read of a SQL Server's live view (ADR 0020, area 3), in words: blocking chains, transactions
/// left open, requests waiting for memory, tempdb filling, and requests running long. Pure logic, tested without a
/// database.
/// </summary>
/// <remarks>
/// Each finding has a title and detail for anyone, written about programs and machines, and an advanced line in SQL
/// Server's terms. The worst comes first. When nothing is wrong, one finding says so, so the Simple view always opens
/// with a sentence.
/// </remarks>
internal static class LiveFindings
{
    /// <summary>A transaction open this long without running anything is worth a look.</summary>
    public const double OpenTransactionCautionSeconds = 300;

    /// <summary>A transaction open this long is a warning.</summary>
    public const double OpenTransactionWarningSeconds = 1800;

    /// <summary>A request running this long is worth a look.</summary>
    public const double LongRequestCautionSeconds = 300;

    /// <summary>A request running this long is a warning.</summary>
    public const double LongRequestWarningSeconds = 1800;

    private const int _maxLongRequests = 3;

    /// <summary>Writes the findings of one read.</summary>
    /// <param name="sessions">The read's sessions, as the live view shows them.</param>
    /// <param name="trees">The read's blocking chains.</param>
    /// <param name="tempdbFreeRatio">The share of tempdb still free, or <see langword="null"/> when unknown.</param>
    /// <param name="grantsWaiting">Requests waiting for memory.</param>
    /// <param name="waits">The wait types explained.</param>
    /// <returns>The findings, the worst first.</returns>
    public static IReadOnlyList<FindingResponse> Build(
        IReadOnlyList<LiveSessionResponse> sessions,
        IReadOnlyList<BlockingNodeResponse> trees,
        double? tempdbFreeRatio,
        int grantsWaiting,
        IReadOnlyDictionary<string, WaitTermResponse> waits)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(waits);

        var byId = sessions.ToDictionary(session => session.Id);
        var findings = new List<FindingResponse>();
        var involved = new HashSet<int>();

        foreach (var tree in trees)
        {
            findings.Add(Blocking(tree, byId, waits));
            Collect(tree, involved);
        }

        findings.AddRange(OpenTransactions(sessions, involved));

        if (grantsWaiting > 0)
        {
            findings.Add(Memory(grantsWaiting, sessions));
        }

        if (tempdbFreeRatio is < 0.1 and { } free)
        {
            findings.Add(Tempdb(free, sessions));
        }

        findings.AddRange(LongRequests(sessions, involved, waits));

        if (findings.Count == 0)
        {
            findings.Add(Calm(sessions));
        }

        return findings
            .OrderBy(finding => finding.Severity switch { "warning" => 0, "caution" => 1, _ => 2 })
            .ToList();
    }

    private static FindingResponse Blocking(
        BlockingNodeResponse tree,
        Dictionary<int, LiveSessionResponse> byId,
        IReadOnlyDictionary<string, WaitTermResponse> waits)
    {
        var waiting = new List<LiveSessionResponse>();
        Descendants(tree, byId, waiting);

        var longest = waiting.Count == 0 ? 0 : waiting.Max(session => session.WaitSeconds);
        var programs = waiting.Select(session => Words.Program(session.Program)).Distinct().Order().ToList();
        var requests = Words.Count(tree.Waiting, "request");
        var verb = tree.Waiting == 1 ? "is" : "are";
        var severity = longest >= 60 || tree.Waiting >= 5 ? "warning" : "caution";
        var resource = waiting.Select(session => session.WaitResource).FirstOrDefault(text => text is not null);
        var waitType = waiting.Select(session => session.WaitType).FirstOrDefault(text => text is not null);
        var waitWords = waitType is not null && waits.TryGetValue(waitType, out var term) ? term.Simple : "wait";

        if (!byId.TryGetValue(tree.Session, out var head))
        {
            return new FindingResponse(
                "blocking",
                severity,
                $"{Capital(requests)} of {Words.List(programs)} {verb} stuck behind a connection that just ended.",
                $"They have waited up to {Words.Duration(longest)}; they should go on in a moment.",
                $"{Words.Count(tree.Waiting, "session")} wait for session {tree.Session}, which this read no longer "
                + "holds.",
                tree.Session,
                []);
        }

        var place = string.IsNullOrWhiteSpace(head.Host) ? "" : $" on {head.Host}";
        var actions = new List<FindingActionResponse>
        {
            new("session", "Open the session", head.Id.ToString(CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrWhiteSpace(head.Program))
        {
            actions.Add(new FindingActionResponse("logs", $"{head.Program}'s logs", head.Program));
        }

        actions.Add(new FindingActionResponse(
            "copy",
            "Copy the statement",
            $"KILL {head.Id};",
            "A DBA runs it to end the connection. Its open transaction is rolled back, which frees the others."));

        if (head.Status == "sleeping")
        {
            var held = head.TransactionSeconds ?? head.IdleSeconds;
            var started = head.ChangedData ? "started a change" : "opened a transaction";
            var holds = head.ChangedData ? "so it still holds the rows" : "and may hold the rows the others need";

            return new FindingResponse(
                "blocking",
                severity,
                $"{Capital(requests)} of {Words.List(programs)} {verb} stuck behind one connection of "
                + $"{Words.Program(head.Program)}{place}.",
                $"That connection {started} {Words.Duration(held)} ago and never finished it, {holds}. The others "
                + $"have waited up to {Words.Duration(longest)}. This usually means the application did not commit "
                + "or roll back after an error.",
                $"Session {head.Id} holds an open transaction for {Words.Clock(held)} and runs nothing; "
                + $"{Words.Count(tree.Waiting, "session")} wait{(tree.Waiting == 1 ? "s" : "")} for its locks"
                + (resource is null ? "." : $" ({waitType} on {resource})."),
                head.Id,
                actions);
        }

        return new FindingResponse(
            "blocking",
            severity,
            $"{Capital(requests)} of {Words.List(programs)} {verb} waiting for {Lower(head.Name)} of "
            + $"{Words.Program(head.Program)}{place}, running for {Words.Duration(head.ElapsedSeconds)}.",
            $"They {waitWords.Replace("waits", "wait", StringComparison.Ordinal)} until it finishes, and have waited "
            + $"up to {Words.Duration(longest)}.",
            $"Session {head.Id} runs {Command(head)} for {Words.Clock(head.ElapsedSeconds)}"
            + (head.WaitType is null ? "" : $", itself waiting on {head.WaitType}")
            + $"; {Words.Count(tree.Waiting, "session")} wait{(tree.Waiting == 1 ? "s" : "")} behind it"
            + (resource is null ? "." : $" ({waitType} on {resource})."),
            head.Id,
            actions);
    }

    private static IEnumerable<FindingResponse> OpenTransactions(
        IReadOnlyList<LiveSessionResponse> sessions,
        HashSet<int> involved)
    {
        var open = sessions
            .Where(session => session.Status == "sleeping"
                && !involved.Contains(session.Id)
                && session.TransactionSeconds >= OpenTransactionCautionSeconds)
            .OrderByDescending(session => session.TransactionSeconds)
            .ToList();

        if (open.Count == 0)
        {
            yield break;
        }

        var oldest = open[0];
        var age = oldest.TransactionSeconds!.Value;
        var place = string.IsNullOrWhiteSpace(oldest.Host) ? "" : $" on {oldest.Host}";
        var more = open.Count == 1 ? "" : $" {Words.Count(open.Count - 1, "other connection")} too.";

        yield return new FindingResponse(
            "open-transaction",
            age >= OpenTransactionWarningSeconds ? "warning" : "caution",
            $"A connection of {Words.Program(oldest.Program)}{place} has had a transaction open for "
            + $"{Words.Duration(age)} and runs nothing.",
            "It blocks nobody yet, but keeps its locks until it commits, and the log cannot be reused past it."
            + more,
            $"Session {oldest.Id} is sleeping with {Words.Count(oldest.OpenTransactions, "open transaction")} for "
            + $"{Words.Clock(age)}" + (oldest.ChangedData ? ", which changed data." : ", which changed nothing yet."),
            oldest.Id,
            [
                new FindingActionResponse(
                    "session",
                    "Open the session",
                    oldest.Id.ToString(CultureInfo.InvariantCulture)),
                new FindingActionResponse(
                    "copy",
                    "Copy the statement",
                    $"KILL {oldest.Id};",
                    "A DBA runs it to end the connection; its transaction is rolled back."),
            ]);
    }

    private static FindingResponse Memory(int grantsWaiting, IReadOnlyList<LiveSessionResponse> sessions)
    {
        var largest = sessions.OrderByDescending(session => session.GrantRequestedBytes).FirstOrDefault();

        return new FindingResponse(
            "memory-grants",
            "caution",
            $"{Capital(Words.Count(grantsWaiting, "request"))} wait{(grantsWaiting == 1 ? "s" : "")} "
            + "for memory to run.",
            "Queries that sort or join much data need memory before they start, and the server has none to give "
            + "now. Usually a few queries ask for far more than they use.",
            $"{grantsWaiting} waiting on RESOURCE_SEMAPHORE"
            + (largest is { GrantRequestedBytes: > 0 }
                ? $"; session {largest.Id} asked for {Bytes(largest.GrantRequestedBytes)}."
                : "."),
            largest?.Id,
            []);
    }

    private static FindingResponse Tempdb(double free, IReadOnlyList<LiveSessionResponse> sessions)
    {
        var used = Math.Round((1 - free) * 100);
        var largest = sessions.OrderByDescending(session => session.TempdbBytes).FirstOrDefault();
        var top = largest is { TempdbBytes: > 0 }
            ? $" The most is held by a connection of {Words.Program(largest.Program)}: {Bytes(largest.TempdbBytes)}."
            : "";

        return new FindingResponse(
            "tempdb",
            free < 0.05 ? "warning" : "caution",
            string.Create(CultureInfo.InvariantCulture, $"tempdb is {used:0}% full."),
            "tempdb holds temporary tables, sorts, and row versions for every database. When it is full, queries fail."
            + top,
            string.Create(CultureInfo.InvariantCulture, $"tempdb data files {used:0}% allocated")
            + (largest is { TempdbBytes: > 0 } ? $"; session {largest.Id} holds {Bytes(largest.TempdbBytes)}." : "."),
            largest is { TempdbBytes: > 0 } ? largest.Id : null,
            []);
    }

    private static IEnumerable<FindingResponse> LongRequests(
        IReadOnlyList<LiveSessionResponse> sessions,
        HashSet<int> involved,
        IReadOnlyDictionary<string, WaitTermResponse> waits) =>
        sessions
            .Where(session => session.Status != "sleeping"
                && !involved.Contains(session.Id)
                && session.PercentComplete is null
                && session.ElapsedSeconds >= LongRequestCautionSeconds)
            .OrderByDescending(session => session.ElapsedSeconds)
            .Take(_maxLongRequests)
            .Select(session => new FindingResponse(
                "long-request",
                session.ElapsedSeconds >= LongRequestWarningSeconds ? "warning" : "caution",
                $"{Capital(session.Name)} of {Words.Program(session.Program)} has run for "
                + $"{Words.Duration(session.ElapsedSeconds)}.",
                session.WaitType is not null && waits.TryGetValue(session.WaitType, out var term)
                    ? $"Right now it {term.Simple}."
                    : "It is busy working, not waiting for anything.",
                $"Session {session.Id} runs {Command(session)} for {Words.Clock(session.ElapsedSeconds)}"
                + (session.WaitType is null ? "." : $", waiting on {session.WaitType}."),
                session.Id,
                [
                    new FindingActionResponse(
                        "session",
                        "Open the session",
                        session.Id.ToString(CultureInfo.InvariantCulture)),
                ]));

    private static FindingResponse Calm(IReadOnlyList<LiveSessionResponse> sessions)
    {
        var running = sessions.Where(session => session.Status != "sleeping").ToList();

        if (running.Count == 0)
        {
            return new FindingResponse(
                "calm",
                "ok",
                "Nothing waits for anything.",
                "No request is running right now.",
                "No active request, no blocking, no open transaction older than 5 minutes.",
                null,
                []);
        }

        var longest = running.Max(session => session.ElapsedSeconds);

        return new FindingResponse(
            "calm",
            "ok",
            "Nothing waits for anything.",
            $"{Capital(Words.Count(running.Count, "request"))} running now, the longest for "
            + $"{Words.Duration(longest)}.",
            $"{running.Count} active request{(running.Count == 1 ? "" : "s")}, no blocking.",
            null,
            []);
    }

    private static void Descendants(
        BlockingNodeResponse node,
        Dictionary<int, LiveSessionResponse> byId,
        List<LiveSessionResponse> into)
    {
        foreach (var child in node.Children)
        {
            if (byId.TryGetValue(child.Session, out var session))
            {
                into.Add(session);
            }

            Descendants(child, byId, into);
        }
    }

    private static void Collect(BlockingNodeResponse node, HashSet<int> into)
    {
        into.Add(node.Session);

        foreach (var child in node.Children)
        {
            Collect(child, into);
        }
    }

    private static string Command(LiveSessionResponse session) =>
        string.IsNullOrWhiteSpace(session.Command) ? "a request" : session.Command;

    private static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.#} GB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0} KB"),
    };

    private static string Capital(string words) =>
        words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];

    // A statement's name in a sentence; a word in capitals, such as a statement's own text, stays as it is.
    private static string Lower(string words) =>
        words.Length == 0 || (words.Length > 1 && char.IsUpper(words[1]))
            ? words
            : char.ToLowerInvariant(words[0]) + words[1..];
}
