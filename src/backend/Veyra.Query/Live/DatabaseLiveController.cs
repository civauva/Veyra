using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Live.Api;

namespace Veyra.Query.Live;

/// <summary>
/// A database server's Live tab (ADR 0020, area 3): what runs, waits, and blocks on it now, or at a moment of the
/// last days.
/// </summary>
[ApiController]
[Route("api/v1/metrics/databases/{engine}/{server}")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class DatabaseLiveController : ControllerBase
{
    private readonly LiveViewBuilder _live;

    /// <summary>Creates the controller.</summary>
    /// <param name="live">Builds the Live tab.</param>
    internal DatabaseLiveController(LiveViewBuilder live)
    {
        _live = live;
    }

    /// <summary>The server's Live tab.</summary>
    /// <param name="engine">The engine, such as <c>microsoft.sql_server</c>.</param>
    /// <param name="server">The server's name.</param>
    /// <param name="at">An ISO 8601 moment to look back at; omitted for now.</param>
    /// <param name="database">Lists only the sessions that use this database.</param>
    /// <param name="program">Lists only the sessions of this program.</param>
    /// <param name="host">Lists only the sessions from this machine.</param>
    /// <param name="login">Lists only the sessions of this login.</param>
    /// <param name="wait">Lists only the sessions whose request waits on this wait type.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The tab; 404 when the server sent nothing; 400 for a moment that is not a time.</returns>
    [HttpGet("live")]
    [ProducesResponseType<LiveResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LiveAsync(
        string engine,
        string server,
        [FromQuery] string? at,
        [FromQuery] string? database,
        [FromQuery] string? program,
        [FromQuery] string? host,
        [FromQuery] string? login,
        [FromQuery] string? wait,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? moment = null;

        if (!string.IsNullOrWhiteSpace(at))
        {
            if (!DateTimeOffset.TryParse(
                at,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
            {
                return Problem(
                    title: "Give the moment as an ISO 8601 time.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            moment = parsed;
        }

        var filter = new LiveFilter(database, program, host, login, wait);

        return await _live.LiveAsync(engine, server, moment, filter, cancellationToken) is { } view
            ? Ok(view)
            : NotFound();
    }
}
