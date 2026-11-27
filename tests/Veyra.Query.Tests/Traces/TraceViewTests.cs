using Veyra.Core.Traces;
using Veyra.Query.Traces;

namespace Veyra.Query.Tests.Traces;

/// <summary>The server works out a trace's figures and its "errors only" view (VEY-67).</summary>
public class TraceViewTests
{
    private static readonly DateTimeOffset _start = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    // checkout (0-100 ms) calls cart (5-20) and pay (25-95); pay calls psp (30-90), which failed with an exception.
    // An orphan, whose parent never arrived, sits apart.
    private static readonly Span _checkout = Make("a1", null, "checkout", 0, 100, SpanStatus.Error);
    private static readonly Span _cart = Make("b2", "a1", "cart", 5, 15);
    private static readonly Span _pay = Make("c3", "a1", "payments", 25, 70, SpanStatus.Error);
    private static readonly Span _psp = Make("d4", "c3", "payments", 30, 60, SpanStatus.Error, exception: true);
    private static readonly Span _orphan = Make("e5", "zz", "mailer", 40, 10);

    private static readonly Span[] _trace = [_checkout, _cart, _pay, _psp, _orphan];

    [Fact]
    public void Build_Summary_CountsTheWholeTrace()
    {
        var view = TraceView.Build("t", _trace, errorsOnly: false);

        Assert.Equal((_start, 100.0), (view.Summary.Start, view.Summary.DurationMs));
        Assert.Equal((5, 3, 1), (view.Summary.Spans, view.Summary.Failed, view.Summary.MissingParents));
        Assert.Equal(["mailer"], view.Summary.MissingParentServices);
        Assert.Equal(
            [("payments", 2), ("cart", 1), ("checkout", 1), ("mailer", 1)],
            view.Summary.Services.Select(service => (service.Service, service.Spans)));
    }

    [Fact]
    public void Build_CriticalPath_FollowsTheChildrenThatDecidedTheDuration()
    {
        var view = TraceView.Build("t", _trace, errorsOnly: false);

        Assert.Equal(["a1", "b2", "c3", "d4"], view.Summary.CriticalPath.Order());
    }

    [Fact]
    public void Build_CriticalPath_LeavesOutChildrenThatOverlapTheWorkAfterThem()
    {
        // b and e overlap the work that finished after them, so making them faster would not have helped.
        Span[] checkout =
        [
            Make("a", null, "web", 0, 1840),
            Make("b", "a", "web", 6, 60),
            Make("c", "a", "web", 52, 1782),
            Make("d", "c", "web", 106, 1612),
            Make("e", "c", "web", 100, 14),
            Make("f", "d", "web", 116, 1500),
            Make("g", "a", "web", 1834, 5),
        ];

        var view = TraceView.Build("t", checkout, errorsOnly: false);

        Assert.Equal(["a", "c", "d", "f", "g"], view.Summary.CriticalPath.Order());
    }

    [Fact]
    public void Build_Cause_IsTheDeepestFailureWithAnException()
    {
        var view = TraceView.Build("t", _trace, errorsOnly: false);

        Assert.Equal("d4", view.Summary.Cause);
    }

    [Fact]
    public void Build_ErrorsOnly_KeepsTheFailuresAndThePathAboveThem()
    {
        var view = TraceView.Build("t", _trace, errorsOnly: true);

        Assert.Equal(["a1", "c3", "d4"], view.Spans.Select(span => span.SpanId));
        Assert.Equal(5, view.Summary.Spans);
    }

    [Fact]
    public void Build_All_ListsEverySpanInTreeOrder()
    {
        var view = TraceView.Build("t", [_psp, _orphan, _checkout, _pay, _cart], errorsOnly: false);

        Assert.Equal(["a1", "b2", "c3", "d4", "e5"], view.Spans.Select(span => span.SpanId));
    }

    [Fact]
    public void Build_Spans_ComeDepthFirstWithTheirDepthChildrenAndMissingParent()
    {
        // b's child f starts after c, so the tree order differs from the order by start (VEY-68).
        Span[] spans =
        [
            Make("c", "a", "web", 25, 10),
            Make("f", "b", "web", 30, 5),
            Make("a", null, "web", 0, 100),
            Make("o", "gone", "mailer", 1, 5),
            Make("b", "a", "web", 5, 40),
        ];

        var view = TraceView.Build("t", spans, errorsOnly: false);

        Assert.Equal(
            [("a", 0, 2, false), ("b", 1, 1, false), ("f", 2, 0, false), ("c", 1, 0, false), ("o", 0, 0, true)],
            view.Spans.Select(span => (span.SpanId, span.Depth, span.Children, span.MissingParent)));
    }

    [Fact]
    public void Build_Attributes_AreSortedByKey()
    {
        var span = Make("a", null, "web", 0, 1) with
        {
            Attributes = new Dictionary<string, string> { ["z.last"] = "1", ["a.first"] = "2", ["m.middle"] = "3" },
        };

        var view = TraceView.Build("t", [span], errorsOnly: false);

        Assert.Equal(["a.first", "m.middle", "z.last"], view.Spans[0].Attributes.Keys);
    }

    private static Span Make(
        string id,
        string? parent,
        string service,
        double startMs,
        double durationMs,
        SpanStatus status = SpanStatus.Unset,
        bool exception = false) =>
        new(
            "t",
            id,
            parent,
            service + " work",
            SpanKind.Internal,
            _start.AddMilliseconds(startMs),
            TimeSpan.FromMilliseconds(durationMs),
            status,
            null,
            service,
            "production",
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            exception ? [new SpanEvent(_start.AddMilliseconds(startMs + 1), "exception", new Dictionary<string, string>())] : [],
            [],
            new SpanDropped(0, 0, 0));
}
