using Veyra.Core.Traces;
using Veyra.Query.Traces.Api;

namespace Veyra.Query.Traces;

/// <summary>
/// Puts one trace together for its screen (VEY-67): the spans in the chosen view and the figures of the whole trace,
/// so the browser counts, ranks, and filters nothing.
/// </summary>
internal static class TraceView
{
    /// <summary>Builds the trace's answer.</summary>
    /// <param name="traceId">The trace ID, in lower-case hex.</param>
    /// <param name="spans">Every span that has arrived, at least one.</param>
    /// <param name="errorsOnly">Whether to keep only the failed spans and every span above them.</param>
    /// <returns>The answer.</returns>
    public static TraceResponse Build(string traceId, IReadOnlyList<Span> spans, bool errorsOnly)
    {
        ArgumentNullException.ThrowIfNull(spans);

        var ordered = spans.OrderBy(span => span.Start).ThenBy(span => span.SpanId, StringComparer.Ordinal).ToList();
        var kept = errorsOnly ? FailedAndAbove(ordered) : null;

        var shown = ordered.Where(span => kept is null || kept.Contains(span.SpanId)).ToList();

        return new TraceResponse(traceId, TreeOrder(shown), Summarize(ordered));
    }

    /// <summary>
    /// Orders spans for the waterfall (VEY-68): depth first from each root, children by start. A span whose parent has
    /// not arrived becomes a root of its own, after the real root, so a gap shows where it is (ADR 0014). Pure.
    /// </summary>
    /// <param name="spans">The spans to show, by start.</param>
    /// <returns>The spans, top to bottom, each with its depth and how many children it has.</returns>
    public static IReadOnlyList<SpanResponse> TreeOrder(IReadOnlyList<Span> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        var ids = spans.Select(span => span.SpanId).ToHashSet(StringComparer.Ordinal);
        var children = spans
            .Where(span => span.ParentSpanId is not null && ids.Contains(span.ParentSpanId))
            .ToLookup(span => span.ParentSpanId!, StringComparer.Ordinal);
        var tops = spans
            .Where(span => span.ParentSpanId is null || !ids.Contains(span.ParentSpanId))
            .OrderBy(span => span.ParentSpanId is not null)
            .ToList();
        var rows = new List<SpanResponse>(spans.Count);
        var waiting = new Stack<(Span Span, int Depth)>();

        for (var index = tops.Count - 1; index >= 0; index--)
        {
            waiting.Push((tops[index], 0));
        }

        // A stack instead of recursion, so a very deep trace cannot run out of stack.
        while (waiting.TryPop(out var next))
        {
            var own = children[next.Span.SpanId].ToList();
            rows.Add(Respond(next.Span) with
            {
                Depth = next.Depth,
                Children = own.Count,
                MissingParent = next.Depth == 0 && next.Span.ParentSpanId is not null,
            });

            for (var index = own.Count - 1; index >= 0; index--)
            {
                waiting.Push((own[index], next.Depth + 1));
            }
        }

        return rows;
    }

    /// <summary>A span as the API answers it.</summary>
    /// <param name="span">The span.</param>
    /// <returns>The answer.</returns>
    public static SpanResponse Respond(Span span) => new(
        span.SpanId,
        span.ParentSpanId,
        span.Name,
        span.Kind,
        span.Start,
        span.Duration.TotalMilliseconds,
        span.Status,
        span.StatusMessage,
        span.ServiceName,
        span.Environment,
        span.HostName,
        Sorted(span.Attributes),
        Sorted(span.ResourceAttributes),
        span.Events,
        span.Links,
        span.Dropped);

    // By key, so every screen lists attributes in one order without sorting them itself (VEY-68).
    private static SortedDictionary<string, string> Sorted(IReadOnlyDictionary<string, string> attributes) =>
        new(attributes.ToDictionary(), StringComparer.Ordinal);

    private static TraceSummaryResponse Summarize(List<Span> spans)
    {
        var ids = spans.Select(span => span.SpanId).ToHashSet(StringComparer.Ordinal);
        var start = spans.Min(span => span.Start);
        var end = spans.Max(span => span.Start + span.Duration);
        var orphans = spans.Where(span => span.ParentSpanId is not null && !ids.Contains(span.ParentSpanId)).ToList();

        return new TraceSummaryResponse(
            start,
            (end - start).TotalMilliseconds,
            spans.Count,
            spans.Count(span => span.Status == SpanStatus.Error),
            orphans.Count,
            [
                .. orphans
                    .Select(span => span.ServiceName)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ],
            [
                .. spans
                    .GroupBy(span => span.ServiceName, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new TraceShareResponse(group.Key, group.Count())),
            ],
            CriticalPath(spans),
            Cause(spans)?.SpanId);
    }

    // From the end of each span on the path, the children that finished before the time reached so far, newest first;
    // a child that overlaps work on the path is left out, since making it faster would not have helped.
    internal static List<string> CriticalPath(List<Span> spans)
    {
        var children = spans
            .Where(span => span.ParentSpanId is not null)
            .ToLookup(span => span.ParentSpanId!, StringComparer.Ordinal);
        var path = new List<string>();
        var root = spans.FirstOrDefault(span => span.ParentSpanId is null) ?? spans[0];
        var waiting = new Stack<Span>([root]);

        while (waiting.TryPop(out var span))
        {
            path.Add(span.SpanId);
            var reached = span.Start + span.Duration;

            foreach (var child in children[span.SpanId].OrderByDescending(child => child.Start + child.Duration))
            {
                if (child.Start + child.Duration <= reached)
                {
                    waiting.Push(child);
                    reached = child.Start;
                }
            }
        }

        return path;
    }

    // The failed span none of whose children failed: the cause rather than the requests that passed it on. Spans with
    // an exception come first, because an exception explains more than a bare "timeout" on the call below it.
    internal static Span? Cause(List<Span> spans)
    {
        var failed = spans.Where(span => span.Status == SpanStatus.Error).ToList();
        var explained = failed.Where(span => span.Events.Any(e => e.Name == "exception")).ToList();
        var pool = explained.Count > 0 ? explained : failed;
        var parents = pool.Select(span => span.ParentSpanId).ToHashSet(StringComparer.Ordinal);

        return pool.FirstOrDefault(span => !parents.Contains(span.SpanId)) ?? pool.FirstOrDefault();
    }

    // The failed spans and every span above them, so "errors only" keeps the path to each failure.
    private static HashSet<string> FailedAndAbove(List<Span> spans)
    {
        var byId = spans.ToDictionary(span => span.SpanId, StringComparer.Ordinal);
        var kept = new HashSet<string>(StringComparer.Ordinal);

        foreach (var failed in spans.Where(span => span.Status == SpanStatus.Error))
        {
            for (var current = failed; current is not null && kept.Add(current.SpanId);)
            {
                current = current.ParentSpanId is { } parent ? byId.GetValueOrDefault(parent) : null;
            }
        }

        return kept;
    }
}
