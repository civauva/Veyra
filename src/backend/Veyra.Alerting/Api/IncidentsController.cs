using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Veyra.Alerting.Incidents;
using Veyra.Core.Security;
using Veyra.Core.Time;

namespace Veyra.Alerting.Api;

/// <summary>
/// Serves the incidents (ADR 0026): the list for the Incidents screen, one incident for its screen and the links in
/// notifications, and acknowledging one. What surrounds an incident is served by the Query module at
/// <c>/api/v1/incidents/{id}/context</c>.
/// </summary>
[ApiController]
[Route("api/v1/incidents")]
[Authorize(Policy = VeyraPolicies.Viewer)]
[AlertingErrors]
public sealed class IncidentsController : ControllerBase
{
    private const int _defaultDays = 30;
    private const int _defaultLimit = 50;
    private const int _maxLimit = 200;

    private readonly IncidentStore _incidents;
    private readonly IncidentDetailsBuilder _details;
    private readonly IOptionsMonitor<AlertingOptions> _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the controller. The Alerting module builds it, so its services can stay internal.</summary>
    /// <param name="incidents">Lists and changes the incidents.</param>
    /// <param name="details">Builds the items and the details.</param>
    /// <param name="options">The Alerting settings with the length of the history.</param>
    /// <param name="clock">The clock that decides what now is.</param>
    internal IncidentsController(
        IncidentStore incidents,
        IncidentDetailsBuilder details,
        IOptionsMonitor<AlertingOptions> options,
        TimeProvider clock)
    {
        _incidents = incidents;
        _details = details;
        _options = options;
        _clock = clock;
    }

    /// <summary>
    /// Lists the incidents that were open at some time in the last days: the open and acknowledged ones first, then
    /// the resolved ones, each the newest first, a page at a time.
    /// </summary>
    /// <param name="state"><c>open</c>, <c>acknowledged</c>, <c>resolved</c>, or <c>all</c>, the default.</param>
    /// <param name="days">How many days back, from 1 to the history kept (90 by default). Defaults to 30.</param>
    /// <param name="environment">Only incidents of this environment.</param>
    /// <param name="limit">The most incidents on the page, 1 to 200. Defaults to 50.</param>
    /// <param name="cursor">The <c>next</c> cursor of the previous page.</param>
    /// <param name="cancellationToken">Cancels the query when the client goes away.</param>
    /// <returns>The page.</returns>
    /// <response code="200">The page.</response>
    /// <response code="400">A filter, the limit, or the cursor is not valid.</response>
    [HttpGet]
    [ProducesResponseType<IncidentListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? state,
        [FromQuery] int? days,
        [FromQuery] string? environment,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        IncidentStatus? status = null;

        if (state is { Length: > 0 } && !string.Equals(state, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (!IncidentStates.TryParse(state, out var parsed))
            {
                return Problem(
                    title: "The state must be open, acknowledged, resolved, or all.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            status = parsed;
        }

        var history = _options.CurrentValue.HistoryDays;
        var count = days ?? Math.Min(_defaultDays, history);

        if (count < 1 || count > history)
        {
            return Problem(
                title: $"The days must be between 1 and {history}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var pageSize = limit ?? _defaultLimit;

        if (pageSize is < 1 or > _maxLimit)
        {
            return Problem(
                title: $"The limit must be between 1 and {_maxLimit}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        IncidentCursor? after = null;

        if (cursor is not null && !IncidentCursor.TryDecode(cursor, out after))
        {
            return Problem(title: "The cursor is not valid.", statusCode: StatusCodes.Status400BadRequest);
        }

        var now = _clock.GetUtcNow();
        var page = await _incidents.ListPageAsync(
            new IncidentListQuery(
                From: TimeZones.LocalMidnight(now, TimeZoneInfo.Utc, count - 1),
                To: now,
                Limit: pageSize,
                Status: status,
                Environment: string.IsNullOrEmpty(environment) ? null : environment,
                After: after),
            cancellationToken);

        return Ok(new IncidentListResponse(
            await _details.ItemsAsync(page.Items, cancellationToken),
            page.Next?.Encode()));
    }

    /// <summary>
    /// Returns one incident with its alerts, figures, timeline, and runbooks, open or resolved within the history kept.
    /// </summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The details.</returns>
    /// <response code="200">The details.</response>
    /// <response code="404">No incident has this ID, or its alerts resolved before the history kept.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<IncidentDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await _details.BuildAsync(id, cancellationToken) is { } details ? Ok(details) : NotFound();

    /// <summary>
    /// Records that the signed-in person is on the incident (ADR 0026). It does not stop notifications. Acknowledging
    /// an acknowledged incident leaves it as it is.
    /// </summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the update when the client goes away.</param>
    /// <returns>The incident as it is now.</returns>
    /// <response code="200">The incident, acknowledged.</response>
    /// <response code="403">The signed-in person is not an Editor or Admin.</response>
    /// <response code="404">No incident has this ID.</response>
    /// <response code="409">The incident is resolved.</response>
    [HttpPost("{id:guid}/acknowledge")]
    [Authorize(Policy = VeyraPolicies.Editor)]
    [ProducesResponseType<IncidentItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcknowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var (userId, name) = Actor();
        var incident = await _incidents.AcknowledgeAsync(id, userId, name, _clock.GetUtcNow(), cancellationToken);

        return incident is null ? NotFound() : Ok(await _details.ItemAsync(incident, cancellationToken));
    }

    /// <summary>Takes the incident's acknowledgement back. An incident nobody acknowledged stays as it is.</summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the update when the client goes away.</param>
    /// <returns>The incident as it is now.</returns>
    /// <response code="200">The incident, open again.</response>
    /// <response code="403">The signed-in person is not an Editor or Admin.</response>
    /// <response code="404">No incident has this ID.</response>
    /// <response code="409">The incident is resolved.</response>
    [HttpDelete("{id:guid}/acknowledge")]
    [Authorize(Policy = VeyraPolicies.Editor)]
    [ProducesResponseType<IncidentItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnacknowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var (userId, name) = Actor();
        var incident = await _incidents.UnacknowledgeAsync(id, userId, name, _clock.GetUtcNow(), cancellationToken);

        return incident is null ? NotFound() : Ok(await _details.ItemAsync(incident, cancellationToken));
    }

    // Who is signed in, from the session's claims (ADR 0008): the user's ID and display name, or their email when the
    // session carries no name.
    private (Guid? Id, string Name) Actor()
    {
        var id = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : (Guid?)null;
        var name = User.FindFirstValue(ClaimTypes.Name) is { Length: > 0 } displayName
            ? displayName
            : User.FindFirstValue(ClaimTypes.Email) ?? "Someone";

        return (id, name);
    }
}
