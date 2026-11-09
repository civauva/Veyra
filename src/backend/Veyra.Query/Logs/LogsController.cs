using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Veyra.Core.Security;
using Veyra.Core.Telemetry;

namespace Veyra.Query.Logs;

/// <summary>
/// Searches stored log records. Backs the Log Explorer, and works the same for scripts.
/// </summary>
[ApiController]
[Route("api/v1/logs")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class LogsController : ControllerBase
{
    // The widest time window one request may scan. Wider searches are slow on large installations and rarely useful.
    private static readonly TimeSpan _maxWindow = TimeSpan.FromDays(31);

    private static readonly TimeSpan _defaultWindow = TimeSpan.FromHours(1);

    private const int _defaultLimit = 100;

    // Round bucket widths for the histogram, from 30 seconds to one day.
    private static readonly TimeSpan[] _bucketSizes =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
    ];

    private readonly ILogSearch _search;
    private readonly QueryOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the controller. MVC calls it for every request.</summary>
    /// <param name="search">The storage module's log search.</param>
    /// <param name="options">The Query settings, for the largest page.</param>
    /// <param name="clock">The clock for the default time window.</param>
    public LogsController(ILogSearch search, IOptions<QueryOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _search = search;
        _options = options.Value;
        _clock = clock;
    }

    /// <summary>Returns one page of log records, newest first.</summary>
    /// <remarks>
    /// Without <paramref name="from"/> and <paramref name="to"/>, the last hour is searched. To page, pass the
    /// <c>nextCursor</c> of the previous answer as <paramref name="cursor"/> with the same filters.
    /// </remarks>
    /// <param name="from">Start of the time window, inclusive, in ISO 8601.</param>
    /// <param name="to">End of the time window, exclusive, in ISO 8601. Defaults to now.</param>
    /// <param name="severity">The lowest severity to include, for example <c>Warning</c>.</param>
    /// <param name="service">Only these services. Repeat the parameter for several.</param>
    /// <param name="text">Words that must all appear in the message, ignoring case.</param>
    /// <param name="traceId">Only records of this trace.</param>
    /// <param name="environment">Only these environments. Repeat the parameter for several.</param>
    /// <param name="attribute">
    /// Only records with this attribute value, written <c>key:value</c>, for example <c>http.route:/pay</c>. Repeat the
    /// parameter to require several.
    /// </param>
    /// <param name="limit">The most records to return, 1 to the configured maximum. Defaults to 100.</param>
    /// <param name="cursor">Where the previous page ended.</param>
    /// <param name="cancellationToken">Cancels the search when the client goes away.</param>
    /// <returns>The page.</returns>
    /// <response code="200">The page, possibly empty.</response>
    /// <response code="400">A parameter is out of range, or the cursor is not one Veyra issued.</response>
    [HttpGet]
    [ProducesResponseType<LogsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] LogSeverity? severity,
        [FromQuery] string[]? service,
        [FromQuery] string? text,
        [FromQuery] string? traceId,
        [FromQuery] string[]? environment,
        [FromQuery] string[]? attribute,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        var pageSize = limit ?? _defaultLimit;

        if (pageSize < 1 || pageSize > _options.MaxPageSize)
        {
            return Problem(
                title: $"The limit must be between 1 and {_options.MaxPageSize}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        LogCursor? after = null;

        if (cursor is not null && !LogCursor.TryDecode(cursor, out after))
        {
            return Problem(title: "The cursor is not valid.", statusCode: StatusCodes.Status400BadRequest);
        }

        var error = TryBuildQuery(from, to, severity, service, text, traceId, environment, attribute, out var query);

        if (error is not null)
        {
            return error;
        }

        var page = await _search.SearchAsync(query! with { Limit = pageSize, After = after }, cancellationToken);

        return Ok(new LogsResponse(page.Items.Select(ToEntry).ToList(), page.Next?.Encode()));
    }

    /// <summary>
    /// Counts the matching records per time bucket and per service, for the histogram and the service filter.
    /// </summary>
    /// <remarks>
    /// Takes the same filters as the search. The bucket width is chosen for about 30 to 40 bars over the window.
    /// The service counts ignore the <paramref name="service"/> filter, so other services can still be added.
    /// </remarks>
    /// <param name="from">Start of the time window, inclusive, in ISO 8601.</param>
    /// <param name="to">End of the time window, exclusive, in ISO 8601. Defaults to now.</param>
    /// <param name="severity">The lowest severity to include.</param>
    /// <param name="service">Only these services. Repeat the parameter for several.</param>
    /// <param name="text">Words that must all appear in the message.</param>
    /// <param name="traceId">Only records of this trace.</param>
    /// <param name="environment">Only these environments.</param>
    /// <param name="attribute">Only records with this attribute value, written <c>key:value</c>.</param>
    /// <param name="cancellationToken">Cancels the counting when the client goes away.</param>
    /// <returns>The buckets and the service counts.</returns>
    /// <response code="200">The summary.</response>
    /// <response code="400">A parameter is out of range.</response>
    [HttpGet("summary")]
    [ProducesResponseType<LogSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummaryAsync(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] LogSeverity? severity,
        [FromQuery] string[]? service,
        [FromQuery] string? text,
        [FromQuery] string? traceId,
        [FromQuery] string[]? environment,
        [FromQuery] string[]? attribute,
        CancellationToken cancellationToken)
    {
        var error = TryBuildQuery(from, to, severity, service, text, traceId, environment, attribute, out var query);

        if (error is not null)
        {
            return error;
        }

        var bucketSize = BucketSizeFor(query!.To - query.From);
        var summary = await _search.SummarizeAsync(query, bucketSize, cancellationToken);

        return Ok(new LogSummaryResponse((int)bucketSize.TotalSeconds, summary.Buckets, summary.Services));
    }

    /// <summary>
    /// Picks a round bucket width that gives at most 40 bars, so the histogram stays readable at any window.
    /// </summary>
    /// <param name="window">The length of the time window.</param>
    /// <returns>The bucket width.</returns>
    internal static TimeSpan BucketSizeFor(TimeSpan window)
    {
        var target = window / 40;

        return _bucketSizes.FirstOrDefault(size => size >= target, _bucketSizes[^1]);
    }

    /// <summary>
    /// Checks the filters both endpoints share and builds the query. Page size and cursor are set by the caller.
    /// </summary>
    /// <returns>A problem response when a filter is invalid; otherwise <see langword="null"/>.</returns>
    private ObjectResult? TryBuildQuery(
        DateTimeOffset? from,
        DateTimeOffset? to,
        LogSeverity? severity,
        string[]? service,
        string? text,
        string? traceId,
        string[]? environment,
        string[]? attribute,
        out LogQuery? query)
    {
        query = null;

        var end = to ?? _clock.GetUtcNow();
        var start = from ?? end - _defaultWindow;

        if (start >= end || end - start > _maxWindow)
        {
            return Problem(
                title: $"The time window must be positive and at most {_maxWindow.TotalDays} days.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryParseAttributes(attribute, out var attributes))
        {
            return Problem(
                title: "Each attribute filter must be written key:value, with a non-empty key.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        query = new LogQuery(start, end, severity, service, text, traceId, _defaultLimit, null, environment, attributes);

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

    private static LogEntryResponse ToEntry(StoredLogRecord stored)
    {
        var record = stored.Record;

        return new LogEntryResponse(
            stored.Id,
            record.Timestamp,
            record.ObservedTimestamp,
            record.Severity,
            record.SeverityText,
            record.Body,
            record.ServiceName,
            record.Environment,
            record.TraceId,
            record.SpanId,
            record.Attributes,
            record.ResourceAttributes);
    }
}
