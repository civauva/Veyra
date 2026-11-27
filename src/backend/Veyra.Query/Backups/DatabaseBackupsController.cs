using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Backups.Api;
using Veyra.Query.Metrics;

namespace Veyra.Query.Backups;

/// <summary>A SQL Server's backup history and freshness (ADR 0025).</summary>
[ApiController]
[Route("api/v1/metrics/databases/{engine}/{server}")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class DatabaseBackupsController : ControllerBase
{
    private readonly BackupsViewBuilder _backups;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller.</summary>
    /// <param name="backups">Builds the Backups tab.</param>
    /// <param name="clock">The clock that decides what now is, for a period that ends now.</param>
    internal DatabaseBackupsController(BackupsViewBuilder backups, TimeProvider clock)
    {
        _backups = backups;
        _clock = clock;
    }

    /// <summary>
    /// The server's backups of the window, their time per day and type, a 7-day grid, and each database's freshness
    /// judged by the backup rules.
    /// </summary>
    /// <param name="engine">The engine, such as <c>microsoft.sql_server</c>.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The window, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="system">Whether the server's own databases are listed; left out when omitted.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The backups.</returns>
    /// <response code="200">The backups.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    /// <response code="404">The server sent neither backups nor backup ages.</response>
    [HttpGet("backups")]
    [ProducesResponseType<BackupsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BackupsAsync(
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

        return await _backups.BackupsAsync(engine, server, span, system, cancellationToken) is { } view
            ? Ok(view)
            : NotFound();
    }
}
