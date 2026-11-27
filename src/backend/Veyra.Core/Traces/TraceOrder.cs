namespace Veyra.Core.Traces;

/// <summary>How the trace list is ordered (ADR 0014, ADR 0029).</summary>
public enum TraceOrder
{
    /// <summary>Newest first, by the root span's start and then its span ID.</summary>
    Newest = 0,

    /// <summary>
    /// Worst first: failed traces, then the slowest, then by trace ID and root span ID, so equally slow traces keep one
    /// order across pages.
    /// </summary>
    Worst = 1,
}
