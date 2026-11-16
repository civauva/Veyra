using Veyra.Core.Activity;
using Veyra.Query.Live.Api;

namespace Veyra.Query.Live;

/// <summary>
/// Builds the blocking chains of one read (ADR 0020, area 3): each head blocker, a session that others wait for and
/// that waits for nobody, with the sessions that wait behind it.
/// </summary>
/// <remarks>
/// A blocker the read does not hold, which happens when it ended between SQL Server's views, still heads its chain by
/// its ID. Sessions that wait for each other in a circle, a deadlock SQL Server has not yet broken, are headed by the
/// lowest ID among them, so every waiting session appears exactly once.
/// </remarks>
internal static class BlockingTrees
{
    /// <summary>Builds the chains.</summary>
    /// <param name="sessions">The read's sessions.</param>
    /// <returns>The chains, the one with the most waiting first.</returns>
    public static IReadOnlyList<BlockingNodeResponse> Build(IReadOnlyList<SessionSample> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var waiters = sessions
            .Where(session => session.BlockedBy > 0 && session.BlockedBy != session.SessionId)
            .GroupBy(session => session.BlockedBy)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(session => session.WaitMilliseconds)
                    .ThenBy(session => session.SessionId)
                    .ToList());

        if (waiters.Count == 0)
        {
            return [];
        }

        var byId = sessions
            .GroupBy(session => session.SessionId)
            .ToDictionary(group => group.Key, group => group.First());
        var placed = new HashSet<int>();
        var trees = new List<BlockingNodeResponse>();

        // Heads first: blockers that wait for nobody, or for a session the read does not hold.
        var heads = waiters.Keys
            .Where(id => !byId.TryGetValue(id, out var session) || session.BlockedBy <= 0 || session.BlockedBy == id)
            .Order();

        foreach (var head in heads)
        {
            trees.Add(Node(head, waiters, placed));
        }

        // What is left waits in a circle; the lowest ID of each circle heads it.
        foreach (var id in waiters.Keys.Order())
        {
            if (!placed.Contains(id))
            {
                trees.Add(Node(id, waiters, placed));
            }
        }

        return trees.OrderByDescending(tree => tree.Waiting).ThenBy(tree => tree.Session).ToList();
    }

    /// <summary>Counts, for every session, how many wait for it directly or through others.</summary>
    /// <param name="trees">The chains.</param>
    /// <returns>Each blocking session's count; sessions that block nobody are not in it.</returns>
    public static Dictionary<int, int> WaitingCounts(IReadOnlyList<BlockingNodeResponse> trees)
    {
        ArgumentNullException.ThrowIfNull(trees);

        var counts = new Dictionary<int, int>();
        var stack = new Stack<BlockingNodeResponse>(trees);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (node.Waiting > 0)
            {
                counts[node.Session] = node.Waiting;
            }

            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }

        return counts;
    }

    private static BlockingNodeResponse Node(
        int id,
        Dictionary<int, List<SessionSample>> waiters,
        HashSet<int> placed)
    {
        placed.Add(id);

        var children = new List<BlockingNodeResponse>();

        foreach (var waiter in waiters.GetValueOrDefault(id) ?? [])
        {
            // A session already placed is a circle closing; it stays where it first appeared.
            if (placed.Contains(waiter.SessionId))
            {
                continue;
            }

            children.Add(Node(waiter.SessionId, waiters, placed));
        }

        return new BlockingNodeResponse(id, children.Sum(child => 1 + child.Waiting), children);
    }
}
