using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Veyra.Core.Security;
using Veyra.Query.Streams.Api;

namespace Veyra.Query.Streams;

/// <summary>
/// Live in the top bar (ADR 0022): a server-sent event stream with what arrives per second, and a tick that tells
/// screens to fetch again.
/// </summary>
[ApiController]
[Route("api/v1/live")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class LiveController : ControllerBase
{
    /// <summary>How often a <c>rate</c> event is sent.</summary>
    internal static readonly TimeSpan RateInterval = TimeSpan.FromSeconds(1);

    // Every fifth rate is followed by a tick, and every fifteenth by a comment.
    private const int _ratesPerTick = 5;

    private readonly LiveTelemetryHub _hub;
    private readonly TimeProvider _clock;
    private readonly JsonSerializerOptions _json;

    /// <summary>Creates the controller. The module builds it, because the hub is its own.</summary>
    /// <param name="hub">The rates of what ingestion writes.</param>
    /// <param name="clock">The clock for the intervals and the tick's time.</param>
    /// <param name="json">The API's JSON settings.</param>
    internal LiveController(LiveTelemetryHub hub, TimeProvider clock, IOptions<JsonOptions> json)
    {
        _hub = hub;
        _clock = clock;
        _json = json.Value.JsonSerializerOptions;
    }

    /// <summary>Streams the rates of logs, spans, and metric points, and a tick every 5 seconds.</summary>
    /// <remarks>
    /// Events: <c>rate</c> at once and then every second, with events per second over the last 10 seconds, such as
    /// <c>{"logs":12.4,"spans":3.1,"points":210}</c>; <c>tick</c> every 5 seconds, <c>{"at":"…"}</c>. A comment
    /// line every 15 seconds keeps proxies from closing the connection. The stream runs until the browser leaves.
    /// </remarks>
    /// <param name="environment">
    /// Only these environments; repeat for several. Without it, everything, with or without an environment.
    /// </param>
    /// <param name="cancellationToken">Ends the stream when the browser goes away.</param>
    /// <returns>The stream, or <c>503</c> when the instance already serves as many streams as allowed.</returns>
    /// <response code="200">The stream, <c>text/event-stream</c>.</response>
    /// <response code="503">As many live streams are open as <c>Veyra:Query:LiveListeners</c> allows.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StreamAsync(
        [FromQuery] string[]? environment,
        CancellationToken cancellationToken)
    {
        using var lease = _hub.TryOpenRates();

        if (lease is null)
        {
            return LiveStreams.Full(this);
        }

        var environments = environment is { Length: > 0 } ? environment : null;

        try
        {
            await ServerSentEvents.StartAsync(HttpContext, cancellationToken);
            await WriteRateAsync(environments, cancellationToken);

            using var timer = new PeriodicTimer(RateInterval, _clock);
            var beats = 0;

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                beats++;
                await WriteRateAsync(environments, cancellationToken);

                if (beats % _ratesPerTick == 0)
                {
                    await ServerSentEvents.WriteEventAsync(
                        Response,
                        "tick",
                        new LiveTickResponse(_clock.GetUtcNow()),
                        _json,
                        cancellationToken);
                }

                if (beats % (int)(ServerSentEvents.HeartbeatInterval / RateInterval) == 0)
                {
                    await ServerSentEvents.WriteHeartbeatAsync(Response, cancellationToken);
                }
            }
        }
        catch (Exception exception) when (LiveStreams.IsDisconnect(exception, cancellationToken))
        {
            // The browser left; the lease frees its place.
        }

        return new EmptyResult();
    }

    private Task WriteRateAsync(IReadOnlyCollection<string>? environments, CancellationToken cancellationToken)
    {
        var rate = _hub.RatesOf(environments);

        return ServerSentEvents.WriteEventAsync(
            Response,
            "rate",
            new LiveRateResponse(rate.Logs, rate.Spans, rate.Points),
            _json,
            cancellationToken);
    }
}
