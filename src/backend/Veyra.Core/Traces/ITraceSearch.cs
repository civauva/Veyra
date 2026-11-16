namespace Veyra.Core.Traces;

/// <summary>Reads traces (ADR 0014). The Storage module implements it; the Query module reads through it.</summary>
public interface ITraceSearch
{
    /// <summary>Lists traces by their root spans, newest first, a page at a time.</summary>
    /// <param name="query">The window, the filters, and the page.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, with each trace's span count, failures, and services.</returns>
    Task<TracePage> ListAsync(TraceQuery query, CancellationToken cancellationToken);

    /// <summary>Reads the durations of the newest traces that match a query, for the list's chart.</summary>
    /// <param name="query">The window and the filters; the page is ignored.</param>
    /// <param name="maxPoints">The most points to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The points and the 95th percentile of the durations read.</returns>
    Task<TraceDurations> DurationsAsync(TraceQuery query, int maxPoints, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the services that received requests in a window, from the newest traces that match a query: at most as
    /// many as the duration chart reads, so the list costs the same however busy the window was.
    /// </summary>
    /// <param name="query">The window and the filters, except the services; the page is ignored.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The services, most traces first.</returns>
    Task<IReadOnlyList<TraceServiceCount>> ServicesAsync(TraceQuery query, CancellationToken cancellationToken);

    /// <summary>Reads every span of a trace.</summary>
    /// <param name="traceId">The trace ID, 32 hex characters.</param>
    /// <param name="around">
    /// When the trace is known to have happened, for example from a log record; its day and the days next to it are
    /// read first. Without it every day kept is read.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The spans, by start time; empty when the trace is unknown.</returns>
    Task<IReadOnlyList<Span>> GetTraceAsync(
        string traceId,
        DateTimeOffset? around,
        CancellationToken cancellationToken);
}
