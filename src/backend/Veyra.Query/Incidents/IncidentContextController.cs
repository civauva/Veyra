using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Incidents.Api;

namespace Veyra.Query.Incidents;

/// <summary>
/// Serves what surrounds an incident (ADR 0026): the path that failed, its exception, other signals, charts, logs,
/// and components. The incident itself is served by the Alerting module at <c>/api/v1/incidents/{id}</c>.
/// </summary>
[ApiController]
[Route("api/v1/incidents")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class IncidentContextController : ControllerBase
{
    private readonly IncidentContextBuilder _context;

    /// <summary>Creates the controller. The Query module builds it, so its builder can stay internal.</summary>
    /// <param name="context">Gathers the context.</param>
    internal IncidentContextController(IncidentContextBuilder context)
    {
        _context = context;
    }

    /// <summary>
    /// Gathers what surrounds an incident over its window: from 30 minutes before it opened to 10 minutes after it
    /// resolved, or now.
    /// </summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The context.</returns>
    /// <response code="200">The context.</response>
    /// <response code="404">No incident has this ID.</response>
    [HttpGet("{id:guid}/context")]
    [ProducesResponseType<IncidentContextResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ContextAsync(Guid id, CancellationToken cancellationToken) =>
        await _context.BuildAsync(id, cancellationToken) is { } context ? Ok(context) : NotFound();
}
