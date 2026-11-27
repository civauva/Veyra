using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
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

    // An export reads this many records at a time, so its memory stays that of one page whatever its size.
    private const int _exportPageSize = 1_000;

    private static readonly Dictionary<LogSeverity, long> _noLevels = [];

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
    private readonly JsonOptions _json;

    /// <summary>Creates the controller. MVC calls it for every request.</summary>
    /// <param name="search">The storage module's log search.</param>
    /// <param name="options">The Query settings, for the largest page and the largest export.</param>
    /// <param name="clock">The clock for the default time window and the export's file name.</param>
    /// <param name="json">The API's JSON settings, so an export's records have the shape a page gives them.</param>
    public LogsController(
        ILogSearch search,
        IOptionsMonitor<QueryOptions> options,
        TimeProvider clock,
        IOptions<JsonOptions> json)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(json);

        _search = search;
        _options = options.CurrentValue;
        _clock = clock;
        _json = json.Value;
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
    /// <param name="spanId">Only records of this span, as a trace's chosen span asks (VEY-67).</param>
    /// <param name="environment">Only these environments. Repeat the parameter for several.</param>
    /// <param name="attribute">
    /// Only records with this attribute value, written <c>key:value</c>, for example <c>http.route:/pay</c>. Repeat the
    /// parameter to require several.
    /// </param>
    /// <param name="host">Only these hosts, by <c>host.name</c>. Repeat the parameter for several (ADR 0024).</param>
    /// <param name="level">Only exactly these levels. Repeat the parameter for several (ADR 0024).</param>
    /// <param name="q">
    /// The query of the log bar, such as <c>service:orders AND level&gt;=warning "timed out"</c>, ANDed with the other
    /// filters (ADR 0024).
    /// </param>
    /// <param name="limit">The most records to return, 1 to the configured maximum. Defaults to 100.</param>
    /// <param name="cursor">Where the previous page ended.</param>
    /// <param name="order">
    /// <c>newest</c> first, the default, or <c>oldest</c> first, as a trace lists its records. Not with words.
    /// </param>
    /// <param name="cancellationToken">Cancels the search when the client goes away.</param>
    /// <returns>The page.</returns>
    /// <response code="200">The page, possibly empty.</response>
    /// <response code="400">
    /// A parameter is out of range, the cursor is not one Veyra issued, or the query does not parse.
    /// </response>
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
        [FromQuery] string? spanId,
        [FromQuery] string[]? environment,
        [FromQuery] string[]? attribute,
        [FromQuery] string[]? host,
        [FromQuery] LogSeverity[]? level,
        [FromQuery] string? q,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        [FromQuery] string? order,
        CancellationToken cancellationToken)
    {
        var pageSize = limit ?? _defaultLimit;
        bool oldestFirst;

        switch (order?.ToLowerInvariant())
        {
            case null or "" or "newest":
                oldestFirst = false;
                break;
            case "oldest":
                oldestFirst = true;
                break;
            default:
                return Problem(
                    title: "The order must be newest or oldest.",
                    statusCode: StatusCodes.Status400BadRequest);
        }

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

        var filters = new LogFilterParameters(
            severity,
            service,
            text,
            traceId,
            spanId,
            environment,
            attribute,
            host,
            level,
            q);

        if (TryBuildQuery(from, to, filters, out var query) is { } error)
        {
            return error;
        }

        // Words are found hour by hour, newest first (ADR 0016), so they have no oldest-first order.
        if (oldestFirst && !string.IsNullOrWhiteSpace(query!.Text))
        {
            return Problem(
                title: "The oldest-first order cannot be combined with text.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var page = await _search.SearchAsync(
            query! with { Limit = pageSize, After = after, OldestFirst = oldestFirst },
            cancellationToken);

        return Ok(new LogsResponse(page.Items.Select(ToEntry).ToList(), page.Next?.Encode()));
    }

    /// <summary>
    /// Counts the matching records per time bucket, per level, and per service, for the histogram and the service
    /// filter.
    /// </summary>
    /// <remarks>
    /// Takes the same filters as the search. The bucket width is chosen for about 30 to 40 bars over the window.
    /// Buckets are aligned to the clock: the first starts with the window and ends at the first round time, so whole
    /// minutes and hours can be read from the counts (ADR 0015). The service counts ignore the
    /// <paramref name="service"/> filter, so other services can still be added.
    /// </remarks>
    /// <param name="from">Start of the time window, inclusive, in ISO 8601.</param>
    /// <param name="to">End of the time window, exclusive, in ISO 8601. Defaults to now.</param>
    /// <param name="severity">The lowest severity to include.</param>
    /// <param name="service">Only these services. Repeat the parameter for several.</param>
    /// <param name="text">Words that must all appear in the message.</param>
    /// <param name="traceId">Only records of this trace.</param>
    /// <param name="spanId">Only records of this span, as a trace's chosen span asks (VEY-67).</param>
    /// <param name="environment">Only these environments.</param>
    /// <param name="attribute">Only records with this attribute value, written <c>key:value</c>.</param>
    /// <param name="host">Only these hosts (ADR 0024).</param>
    /// <param name="level">Only exactly these levels (ADR 0024).</param>
    /// <param name="q">The query of the log bar, ANDed with the other filters (ADR 0024).</param>
    /// <param name="cancellationToken">Cancels the counting when the client goes away.</param>
    /// <returns>The buckets and the service counts.</returns>
    /// <response code="200">The summary.</response>
    /// <response code="400">A parameter is out of range, or the query does not parse.</response>
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
        [FromQuery] string? spanId,
        [FromQuery] string[]? environment,
        [FromQuery] string[]? attribute,
        [FromQuery] string[]? host,
        [FromQuery] LogSeverity[]? level,
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var filters = new LogFilterParameters(
            severity,
            service,
            text,
            traceId,
            spanId,
            environment,
            attribute,
            host,
            level,
            q);

        if (TryBuildQuery(from, to, filters, out var query) is { } error)
        {
            return error;
        }

        var bucketSize = BucketSizeFor(query!.To - query.From);
        var summary = await _search.SummarizeAsync(query, bucketSize, cancellationToken);
        var levels = summary.LevelsPerBucket;

        var buckets = summary.Buckets
            .Select((bucket, index) => new LogBucketResponse(
                bucket.Start,
                bucket.Total,
                bucket.Errors,
                Names(levels is null ? _noLevels : levels[index])))
            .ToList();
        var totals = (levels ?? [])
            .SelectMany(bucket => bucket)
            .GroupBy(level => level.Key)
            .ToDictionary(group => group.Key, group => group.Sum(level => level.Value));

        return Ok(new LogSummaryResponse(
            (int)bucketSize.TotalSeconds,
            buckets,
            summary.Services,
            summary.Buckets.Sum(bucket => bucket.Total),
            summary.Buckets.Sum(bucket => bucket.Errors),
            Names(totals)));
    }

    /// <summary>
    /// Splits the query of the log bar into its terms, each with its place in the text, so the chips can show and edit
    /// it without parsing it themselves (ADR 0024).
    /// </summary>
    /// <param name="q">The query.</param>
    /// <returns>The terms.</returns>
    /// <response code="200">The terms; none for an empty query.</response>
    /// <response code="400">
    /// The query does not parse; <c>position</c> and <c>expected</c> say where and what was expected.
    /// </response>
    [HttpGet("parse")]
    [ProducesResponseType<LogQueryParseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Parse([FromQuery] string? q)
    {
        var parse = LogQueryParser.Parse(q);

        if (parse.Error is { } error)
        {
            return LogRequests.Problem(this, new LogFilterProblem(error.Message, error.Position, error.Expected));
        }

        return Ok(new LogQueryParseResponse(parse.Terms
            .Select(term => new LogQueryTermResponse(
                term.Field.ToString().ToLowerInvariant(),
                term.Op,
                term.Value,
                term.Start,
                term.End,
                term.Key))
            .ToList()));
    }

    /// <summary>
    /// Writes every matching record as a file, newest first, up to <c>Veyra:Query:MaxExportRecords</c> (ADR 0024).
    /// </summary>
    /// <remarks>
    /// Takes the filters of the search. The records are read page by page with the search's cursor and written as
    /// they come, so memory stays flat. When more records match than the limit, the file ends with a line that says
    /// so: <c>{"truncated":true,"limit":N}</c> in NDJSON, <c># truncated at N records</c> in CSV.
    /// </remarks>
    /// <param name="from">Start of the time window, inclusive, in ISO 8601.</param>
    /// <param name="to">End of the time window, exclusive, in ISO 8601. Defaults to now.</param>
    /// <param name="severity">The lowest severity to include.</param>
    /// <param name="service">Only these services. Repeat the parameter for several.</param>
    /// <param name="text">Words that must all appear in the message.</param>
    /// <param name="traceId">Only records of this trace.</param>
    /// <param name="spanId">Only records of this span.</param>
    /// <param name="environment">Only these environments.</param>
    /// <param name="attribute">Only records with this attribute value, written <c>key:value</c>.</param>
    /// <param name="host">Only these hosts.</param>
    /// <param name="level">Only exactly these levels.</param>
    /// <param name="q">The query of the log bar, ANDed with the other filters.</param>
    /// <param name="format"><c>ndjson</c>, the default, or <c>csv</c>.</param>
    /// <param name="cancellationToken">Stops the export when the client goes away.</param>
    /// <returns>The file, as an attachment.</returns>
    /// <response code="200">The file.</response>
    /// <response code="400">A parameter is out of range, the format is unknown, or the query does not parse.</response>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] LogSeverity? severity,
        [FromQuery] string[]? service,
        [FromQuery] string? text,
        [FromQuery] string? traceId,
        [FromQuery] string? spanId,
        [FromQuery] string[]? environment,
        [FromQuery] string[]? attribute,
        [FromQuery] string[]? host,
        [FromQuery] LogSeverity[]? level,
        [FromQuery] string? q,
        [FromQuery] string? format,
        CancellationToken cancellationToken)
    {
        var filters = new LogFilterParameters(
            severity,
            service,
            text,
            traceId,
            spanId,
            environment,
            attribute,
            host,
            level,
            q);

        if (TryBuildQuery(from, to, filters, out var query) is { } error)
        {
            return error;
        }

        // Checked before anything is written, so a wrong format is still answered with a problem.
        if (LogExportWriter.For(format, TextWriter.Null, _json.JsonSerializerOptions) is null)
        {
            return Problem(title: "The format must be ndjson or csv.", statusCode: StatusCodes.Status400BadRequest);
        }

        await using var body = new StreamWriter(Response.Body, new UTF8Encoding(false), leaveOpen: true);
        var writer = LogExportWriter.For(format, body, _json.JsonSerializerOptions)!;
        var limit = _options.MaxExportRecords;
        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"veyra-logs-{_clock.GetUtcNow():yyyyMMdd'T'HHmmss'Z'}.{writer.Extension}");

        Response.ContentType = writer.ContentType;
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = name }
            .ToString();

        try
        {
            await WriteExportAsync(writer, body, query!, limit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser left; the file it got is cut, and nobody is there to tell.
        }

        return new EmptyResult();
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

    /// <summary>Shapes a record as the API returns it, here, in the export, and in the live stream of records.</summary>
    /// <param name="stored">The record and its ID.</param>
    /// <returns>The entry.</returns>
    internal static LogEntryResponse ToEntry(StoredLogRecord stored)
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
            record.HostName,
            record.TraceId,
            record.SpanId,
            record.Attributes,
            record.ResourceAttributes);
    }

    private static Dictionary<string, long> Names(IReadOnlyDictionary<LogSeverity, long> levels) =>
        levels
            .Where(level => level.Value > 0)
            .OrderBy(level => level.Key)
            .ToDictionary(level => level.Key.ToString(), level => level.Value, StringComparer.Ordinal);

    /// <summary>Writes the pages of an export until the records or the limit run out.</summary>
    private async Task WriteExportAsync(
        LogExportWriter writer,
        StreamWriter body,
        LogQuery query,
        int limit,
        CancellationToken cancellationToken)
    {
        await writer.WriteHeaderAsync(cancellationToken);

        var written = 0;
        LogCursor? after = null;

        while (true)
        {
            var page = await _search.SearchAsync(
                query with { Limit = Math.Min(_exportPageSize, limit - written), After = after },
                cancellationToken);

            foreach (var item in page.Items)
            {
                await writer.WriteAsync(ToEntry(item), cancellationToken);
            }

            written += page.Items.Count;
            await body.FlushAsync(cancellationToken);

            if (page.Next is null)
            {
                break;
            }

            // A cursor means more records match; at the limit, the file says it is cut short.
            if (written >= limit)
            {
                await writer.WriteTruncatedAsync(limit, cancellationToken);

                break;
            }

            after = page.Next;
        }
    }

    /// <summary>
    /// Checks the time window and the filters every endpoint shares, and builds the query. Page size, cursor, and
    /// order are set by the caller.
    /// </summary>
    /// <returns>A problem response when a filter is invalid; otherwise <see langword="null"/>.</returns>
    private ObjectResult? TryBuildQuery(
        DateTimeOffset? from,
        DateTimeOffset? to,
        LogFilterParameters filters,
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

        if (LogRequests.TryBuild(filters, start, end, out query) is { } problem)
        {
            return LogRequests.Problem(this, problem);
        }

        query = query! with { Limit = _defaultLimit };

        return null;
    }
}
