using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Blocking.Api;
using Veyra.Query.Metrics;

namespace Veyra.Query.Blocking;

/// <summary>A database server's blocking episodes and deadlocks over a range (ADR 0020, area 5).</summary>
[ApiController]
[Route("api/v1/metrics/databases/{engine}/{server}")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class DatabaseBlockingController : ControllerBase
{
    private readonly BlockingViewBuilder _blocking;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller.</summary>
    /// <param name="blocking">Builds the Blocking tab.</param>
    /// <param name="clock">The clock that decides what now is, for a period that ends now.</param>
    internal DatabaseBlockingController(BlockingViewBuilder blocking, TimeProvider clock)
    {
        _blocking = blocking;
        _clock = clock;
    }

    /// <summary>The server's Blocking tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The range, such as <c>24h</c>, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range (ADR 0022).</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The episodes and deadlocks.</returns>
    /// <response code="200">The episodes and deadlocks.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    [HttpGet("blocking")]
    [ProducesResponseType<BlockingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BlockingAsync(
        string engine,
        string server,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        return Ok(await _blocking.BlockingAsync(engine, server, span, cancellationToken));
    }
}
