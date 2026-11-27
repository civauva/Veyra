using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Veyra.Core.Security;
using Veyra.Core.Telemetry;
using Veyra.Query.Logs;
using Veyra.Query.Streams.Api;

namespace Veyra.Query.Streams;

/// <summary>
/// New log records as they are written, filtered on the server (ADR 0022), for the Log Explorer's Live.
/// </summary>
[ApiController]
[Route("api/v1/logs/stream")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class LogStreamController : ControllerBase
{
    // Records that wait are sent together, up to this many before the next flush, so a burst is a few writes.
    private const int _recordsPerFlush = 200;

    private readonly LiveTelemetryHub _hub;
    private readonly TimeProvider _clock;
    private readonly JsonSerializerOptions _json;

    /// <summary>Creates the controller. The module builds it, because the hub is its own.</summary>
    /// <param name="hub">The records ingestion writes.</param>
    /// <param name="clock">The clock for the heartbeat.</param>
    /// <param name="json">
    /// The API's JSON settings, so records have the shape <c>GET /api/v1/logs</c> gives them.
    /// </param>
    internal LogStreamController(LiveTelemetryHub hub, TimeProvider clock, IOptions<JsonOptions> json)
    {
        _hub = hub;
        _clock = clock;
        _json = json.Value.JsonSerializerOptions;
    }

    /// <summary>Streams every new record that matches the filters, in the order written.</summary>
    /// <remarks>
    /// Takes the filters of <c>GET /api/v1/logs</c>; there is no window, page, or order, because only records written
    /// from now on are sent. Events: <c>record</c> with one record as the logs API shapes it, whose <c>id</c> is its
    /// number on this instance and grows in write order; <c>skipped</c>, <c>{"count":N}</c>, when records that matched
    /// were dropped because the browser fell behind its queue of 1,000. A comment line is sent when the stream has been
    /// quiet for 15 seconds.
    /// </remarks>
    /// <param name="severity">The lowest severity to include, for example <c>Warning</c>.</param>
    /// <param name="service">Only these services. Repeat the parameter for several.</param>
    /// <param name="text">Words that must all appear in the message, ignoring case.</param>
    /// <param name="traceId">Only records of this trace.</param>
    /// <param name="spanId">Only records of this span.</param>
    /// <param name="environment">Only these environments. Repeat the parameter for several.</param>
    /// <param name="attribute">
    /// Only records with this attribute value, written <c>key:value</c>; repeat for several.
    /// </param>
    /// <param name="host">Only these hosts. Repeat the parameter for several (ADR 0024).</param>
    /// <param name="level">Only exactly these levels. Repeat the parameter for several (ADR 0024).</param>
    /// <param name="q">The query of the log bar, ANDed with the other filters (ADR 0024).</param>
    /// <param name="cancellationToken">Ends the stream when the browser goes away.</param>
    /// <returns>
    /// The stream; <c>400</c> for a broken filter; <c>503</c> when the instance serves as many as allowed.
    /// </returns>
    /// <response code="200">The stream, <c>text/event-stream</c>.</response>
    /// <response code="400">
    /// An attribute filter is not written <c>key:value</c>, or the query does not parse.
    /// </response>
    /// <response code="503">As many live streams are open as <c>Veyra:Query:LiveListeners</c> allows.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StreamAsync(
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
        var now = _clock.GetUtcNow();
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

        if (LogRequests.TryBuild(filters, now, now, out var query) is { } problem)
        {
            return LogRequests.Problem(this, problem);
        }

        var filter = LiveLogFilter.From(query!);

        using var listener = _hub.TryOpenLogs(filter);

        if (listener is null)
        {
            return LiveStreams.Full(this);
        }

        try
        {
            await ServerSentEvents.StartAsync(HttpContext, cancellationToken);
            await SendAsync(listener, cancellationToken);
        }
        catch (Exception exception) when (LiveStreams.IsDisconnect(exception, cancellationToken))
        {
            // The browser left; disposing the listener takes it off the hub.
        }

        return new EmptyResult();
    }

    private async Task SendAsync(LiveLogListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await WaitAsync(listener, cancellationToken))
            {
                if (listener.Records.Completion.IsCompleted)
                {
                    return;
                }

                await WriteSkippedAsync(listener, cancellationToken);
                await ServerSentEvents.WriteHeartbeatAsync(Response, cancellationToken);

                continue;
            }

            // The skipped records were older than those still waiting, so they are told first.
            await WriteSkippedAsync(listener, cancellationToken);

            var sent = 0;

            while (sent < _recordsPerFlush && listener.Records.TryRead(out var item))
            {
                await ServerSentEvents.WriteEventAsync(
                    Response,
                    "record",
                    LogsController.ToEntry(new StoredLogRecord(item.Sequence, item.Record)),
                    _json,
                    cancellationToken,
                    flush: false);
                sent++;
            }

            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    // True when records wait; false when the stream was quiet for a heartbeat's length.
    private async Task<bool> WaitAsync(LiveLogListener listener, CancellationToken cancellationToken)
    {
        using var quiet = new CancellationTokenSource(ServerSentEvents.HeartbeatInterval, _clock);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quiet.Token);

        try
        {
            return await listener.Records.WaitToReadAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task WriteSkippedAsync(LiveLogListener listener, CancellationToken cancellationToken)
    {
        var skipped = listener.TakeSkipped();

        if (skipped > 0)
        {
            await ServerSentEvents.WriteEventAsync(
                Response,
                "skipped",
                new LiveSkippedResponse(skipped),
                _json,
                cancellationToken,
                flush: false);
        }
    }
}
