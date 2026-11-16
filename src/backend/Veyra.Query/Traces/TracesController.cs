using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Veyra.Core.Security;
using Veyra.Core.Traces;
using Veyra.Query.Traces.Api;

namespace Veyra.Query.Traces;

/// <summary>
/// The trace list, its duration chart, the services in a window, and single traces (ADR 0014).
/// </summary>
/// <remarks>
/// Every request is bounded in time, so it reads only the days of its window. The list pages by keyset; the chart and
/// the services read at most the newest 20,000 traces; a trace opened from a log record or the list carries the time it
/// is known from, so it is looked up in that day and its neighbours first.
/// </remarks>
[ApiController]
[Route("api/v1/traces")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class TracesController : ControllerBase
{
    // As for logs: wider windows are slow on large installations and longer than traces are usually kept.
    private static readonly TimeSpan _maxWindow = TimeSpan.FromDays(31);

    private static readonly TimeSpan _defaultWindow = TimeSpan.FromHours(1);

    private const int _defaultLimit = 50;

    // About one point per pixel of a wide chart; failed and slow traces are always among them.
    private const int _chartPoints = 1_500;

    private readonly ITraceSearch _search;
    private readonly QueryOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the controller.</summary>
    /// <param name="search">Reads traces from storage.</param>
    /// <param name="options">The page size limit.</param>
    /// <param name="clock">The clock for the default window.</param>
    public TracesController(ITraceSearch search, IOptionsMonitor<QueryOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _search = search;
        _options = options.CurrentValue;
        _clock = clock;
    }

    /// <summary>Lists traces by their root spans, newest first.</summary>
    /// <param name="filters">The window and the filters.</param>
    /// <param name="slow">Only traces at least as slow as the 95th percentile of the window.</param>
    /// <param name="limit">The page size; 50 when omitted.</param>
    /// <param name="cursor">The token from the previous page, or none for the newest traces.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page, or 400 when a filter is invalid.</returns>
    [HttpGet]
    [ProducesResponseType<TraceListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] TraceFilters filters,
        [FromQuery] bool? slow,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var pageSize = limit ?? _defaultLimit;

        if (pageSize < 1 || pageSize > _options.MaxPageSize)
        {
            return Problem(
                title: $"The limit must be between 1 and {_options.MaxPageSize}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        TraceCursor? after = null;

        if (cursor is not null && !TraceCursor.TryDecode(cursor, out after))
        {
            return Problem(title: "The cursor is not valid.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (TryBuildQuery(filters, out var query) is { } error)
        {
            return error;
        }

        query = query! with { Limit = pageSize, After = after };

        if (slow == true)
        {
            // Slow means as slow as the slowest 5% of the window; the same reading the chart draws its line from.
            var durations = await _search.DurationsAsync(query, 0, cancellationToken);

            if (durations.P95 is not { } p95)
            {
                return Ok(new TraceListResponse([], null));
            }

            query = query with { MinDuration = query.MinDuration is { } min && min > p95 ? min : p95 };
        }

        var page = await _search.ListAsync(query, cancellationToken);

        return Ok(new TraceListResponse(page.Items.Select(ToItem).ToList(), page.Next?.Encode()));
    }

    /// <summary>The durations of the newest traces of the window, for the list's chart.</summary>
    /// <param name="filters">The window and the filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The points and the 95th percentile, or 400 when a filter is invalid.</returns>
    [HttpGet("durations")]
    [ProducesResponseType<TraceDurationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DurationsAsync(
        [FromQuery] TraceFilters filters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (TryBuildQuery(filters, out var query) is { } error)
        {
            return error;
        }

        var durations = await _search.DurationsAsync(query!, _chartPoints, cancellationToken);

        return Ok(new TraceDurationsResponse(
            durations.Points
                .Select(point => new TraceDurationPointResponse(
                    point.TraceId,
                    point.Start,
                    point.Duration.TotalMilliseconds,
                    point.Failed))
                .ToList(),
            durations.Read,
            20_000,
            durations.P95?.TotalMilliseconds));
    }

    /// <summary>The services that received requests in the window, most traces first, for the filter panel.</summary>
    /// <param name="filters">The window and the filters; a service filter is ignored.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The services, or 400 when a filter is invalid.</returns>
    [HttpGet("services")]
    [ProducesResponseType<IReadOnlyList<TraceServiceResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ServicesAsync(
        [FromQuery] TraceFilters filters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (TryBuildQuery(filters, out var query) is { } error)
        {
            return error;
        }

        var services = await _search.ServicesAsync(query!, cancellationToken);

        return Ok(services.Select(service => new TraceServiceResponse(service.Service, service.Traces)).ToList());
    }

    /// <summary>Opens a trace with every span that has arrived.</summary>
    /// <param name="traceId">The trace ID, 32 hex characters.</param>
    /// <param name="at">
    /// When the trace is known to have happened; its day and the days next to it are read first.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The trace, 400 for a malformed ID, or 404 when no span of it is stored.</returns>
    [HttpGet("{traceId}")]
    [ProducesResponseType<TraceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        string traceId,
        [FromQuery] DateTimeOffset? at,
        CancellationToken cancellationToken)
    {
        if (traceId is not { Length: 32 } || !traceId.All(char.IsAsciiHexDigit))
        {
            return Problem(
                title: "A trace ID is 32 hexadecimal characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var id = traceId.ToLowerInvariant();
        var spans = await _search.GetTraceAsync(id, at, cancellationToken);

        if (spans.Count == 0)
        {
            return Problem(
                title: "No span of this trace is stored. It may be older than the traces kept, or not sampled.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(new TraceResponse(id, spans.Select(ToSpan).ToList()));
    }

    private ObjectResult? TryBuildQuery(TraceFilters filters, out TraceQuery? query)
    {
        query = null;

        var end = filters.To ?? _clock.GetUtcNow();
        var start = filters.From ?? end - _defaultWindow;

        if (start >= end || end - start > _maxWindow)
        {
            return Problem(
                title: $"The time window must be positive and at most {_maxWindow.TotalDays} days.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (filters.MinDurationMs is < 0 || filters.MaxDurationMs is < 0)
        {
            return Problem(title: "Durations cannot be negative.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryParseAttributes(filters.Attribute, out var attributes))
        {
            return Problem(
                title: "Each attribute filter must be written key:value, with a non-empty key.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        query = new TraceQuery(
            start,
            end,
            filters.Service is { Length: > 0 } services ? services : null,
            filters.Environment is { Length: > 0 } environments ? environments : null,
            filters.Failed == true,
            filters.MinDurationMs is { } min ? TimeSpan.FromMilliseconds(min) : null,
            filters.MaxDurationMs is { } max ? TimeSpan.FromMilliseconds(max) : null,
            string.IsNullOrWhiteSpace(filters.Name) ? null : filters.Name,
            attributes);

        return null;
    }

    // Splits at the first colon, so values may contain colons, as URLs and times do.
    private static bool TryParseAttributes(string[]? filters, out Dictionary<string, string>? attributes)
    {
        attributes = null;

        if (filters is not { Length: > 0 })
        {
            return true;
        }

        attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var filter in filters)
        {
            var separator = filter.IndexOf(':', StringComparison.Ordinal);

            if (separator < 1)
            {
                return false;
            }

            attributes[filter[..separator]] = filter[(separator + 1)..];
        }

        return true;
    }

    private static TraceItemResponse ToItem(TraceSummary trace) => new(
        trace.TraceId,
        trace.RootSpanId,
        trace.Name,
        trace.ServiceName,
        trace.Environment,
        trace.Start,
        trace.Duration.TotalMilliseconds,
        trace.Failed,
        trace.Spans,
        trace.FailedSpans,
        trace.Cause is { } cause ? new TraceCauseResponse(cause.Service, cause.SpanName, cause.Message) : null,
        trace.Services.Select(share => new TraceShareResponse(share.Service, share.Spans)).ToList());

    private static SpanResponse ToSpan(Span span) => new(
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
        span.Attributes,
        span.ResourceAttributes,
        span.Events,
        span.Links,
        span.Dropped);
}
