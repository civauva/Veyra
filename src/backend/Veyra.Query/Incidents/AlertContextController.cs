using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Incidents.Api;

namespace Veyra.Query.Incidents;

/// <summary>
/// Serves what surrounds one alert (ADR 0029): the blocks it touches and the logs around its opening. The alert itself
/// is served by the Alerting module at <c>/api/v1/alerts/{id}</c>.
/// </summary>
[ApiController]
[Route("api/v1/alerts")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class AlertContextController : ControllerBase
{
    private readonly AlertContextBuilder _context;

    /// <summary>Creates the controller. The Query module builds it, so its builder can stay internal.</summary>
    /// <param name="context">Gathers the context.</param>
    internal AlertContextController(AlertContextBuilder context)
    {
        _context = context;
    }

    /// <summary>
    /// Gathers what surrounds an alert over its window: from an hour before it opened until it resolved, or now.
    /// </summary>
    /// <param name="id">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The context.</returns>
    /// <response code="200">The context.</response>
    /// <response code="404">No alert has this ID.</response>
    [HttpGet("{id:guid}/context")]
    [ProducesResponseType<AlertContextResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ContextAsync(Guid id, CancellationToken cancellationToken) =>
        await _context.BuildAsync(id, cancellationToken) is { } context ? Ok(context) : NotFound();
}
