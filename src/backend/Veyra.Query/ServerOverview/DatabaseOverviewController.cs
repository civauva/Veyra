using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Metrics;
using Veyra.Query.ServerOverview.Api;

namespace Veyra.Query.ServerOverview;

/// <summary>A SQL Server's Overview tab in one request (ADR 0025).</summary>
[ApiController]
[Route("api/v1/metrics/databases/{engine}/{server}")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class DatabaseOverviewController : ControllerBase
{
    private readonly ServerOverviewBuilder _overview;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller.</summary>
    /// <param name="overview">Builds the Overview.</param>
    /// <param name="clock">The clock that decides what now is, for a period that ends now.</param>
    internal DatabaseOverviewController(ServerOverviewBuilder overview, TimeProvider clock)
    {
        _overview = overview;
        _clock = clock;
    }

    /// <summary>
    /// The server's machine, figures with their change, performance, counters, I/O, waits, storage, inventory,
    /// blocking, and coverage, all of one moment.
    /// </summary>
    /// <param name="engine">The engine, such as <c>microsoft.sql_server</c>.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The window, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="system">
    /// Whether the server's own databases are listed in the inventory and the largest databases; left out when omitted.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The Overview.</returns>
    /// <response code="200">The Overview.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    /// <response code="404">The server sent neither metrics nor live reads.</response>
    [HttpGet("overview")]
    [ProducesResponseType<ServerOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OverviewAsync(
        string engine,
        string server,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool system,
        CancellationToken cancellationToken)
    {
        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        return await _overview.OverviewAsync(engine, server, span, system, cancellationToken) is { } view
            ? Ok(view)
            : NotFound();
    }
}
