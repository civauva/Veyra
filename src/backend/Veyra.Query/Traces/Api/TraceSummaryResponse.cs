namespace Veyra.Query.Traces.Api;

/// <summary>
/// The figures of a whole trace, worked out by the server (VEY-67). They stay those of every span when the spans are
/// narrowed to the failures, so the header and the waterfall's scale do not change with the view.
/// </summary>
/// <param name="Start">When the first span started.</param>
/// <param name="DurationMs">From the start of the first span to the end of the last, in milliseconds.</param>
/// <param name="Spans">How many spans have arrived.</param>
/// <param name="Failed">How many of them ended in an error.</param>
/// <param name="MissingParents">How many name a parent that has not arrived (ADR 0014).</param>
/// <param name="MissingParentServices">The services of those spans, by name.</param>
/// <param name="Services">The spans per service, most first, then by name.</param>
/// <param name="CriticalPath">
/// The spans that decided how long the request took: from the end of each span on the path, the children that finished
/// before the time reached so far, newest first.
/// </param>
/// <param name="Cause">
/// The failed span none of whose children failed, spans with an exception first; null when nothing failed.
/// </param>
public sealed record TraceSummaryResponse(
    DateTimeOffset Start,
    double DurationMs,
    int Spans,
    int Failed,
    int MissingParents,
    IReadOnlyList<string> MissingParentServices,
    IReadOnlyList<TraceShareResponse> Services,
    IReadOnlyList<string> CriticalPath,
    string? Cause);
