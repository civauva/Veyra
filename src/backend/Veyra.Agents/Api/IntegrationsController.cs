using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Agents.Integrations;
using Veyra.Agents.Probes;
using Veyra.Agents.SqlServers;
using Veyra.Core.Security;

namespace Veyra.Agents.Api;

/// <summary>
/// The Integrations screens (ADR 0028): each monitored SQL Server as an integration with its package page, template
/// versions, and connection probes through an agent. Administrators only: every answer names a login or the
/// connection, and probes carry passwords. Passwords go in and never come back.
/// </summary>
[ApiController]
[Route("api/v1/integrations")]
[Authorize(Policy = VeyraPolicies.Admin)]
[AgentsErrors]
public sealed class IntegrationsController : ControllerBase
{
    private readonly IntegrationService _integrations;

    private readonly SqlServerService _servers;

    private readonly ProbeService _probes;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller. The Agents module builds it, so its services can stay internal.</summary>
    /// <param name="integrations">Reads the integrations.</param>
    /// <param name="servers">Moves a server to another template version.</param>
    /// <param name="probes">Runs connection probes.</param>
    /// <param name="clock">The current time, for a window that ends now.</param>
    internal IntegrationsController(
        IntegrationService integrations,
        SqlServerService servers,
        ProbeService probes,
        TimeProvider clock)
    {
        _integrations = integrations;
        _servers = servers;
        _probes = probes;
        _clock = clock;
    }

    /// <summary>Lists every integration with its state.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The integrations, by name.</returns>
    [HttpGet]
    public async Task<IntegrationListResponse> ListAsync(CancellationToken cancellationToken) =>
        new(await _integrations.ListAsync(cancellationToken));

    /// <summary>Lists the modules of the SQL Server template, with their intervals, for the setup wizard.</summary>
    /// <returns>The modules, in order.</returns>
    [HttpGet("modules/sqlserver")]
    public ActionResult<IReadOnlyList<ModuleCatalogResponse>> Modules() => Ok(IntegrationService.Modules());

    /// <summary>Reads a SQL Server integration's package page.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="range">
    /// The window, ending now: <c>15m</c>, <c>1h</c>, <c>6h</c>, <c>24h</c>, <c>7d</c>, or <c>30d</c>.
    /// </param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window; now when left out.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page.</returns>
    /// <response code="200">The page.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    /// <response code="404">No server has this ID.</response>
    [HttpGet("sqlserver/{id:guid}")]
    [ProducesResponseType<IntegrationDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DetailAsync(
        Guid id,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (IntegrationWindow.Resolve(range, from, to, _clock.GetUtcNow()) is not { } window)
        {
            return Problem(title: IntegrationWindow.Refusal, statusCode: StatusCodes.Status400BadRequest);
        }

        return await _integrations.DetailAsync(id, window, cancellationToken) is { } detail
            ? Ok(detail)
            : NotFound();
    }

    /// <summary>
    /// Moves a SQL Server integration to a template version Veyra ships: the latest (Update) or the previous one
    /// (Roll back). Its agent reads with it from its next poll; the change is in the monitoring history.
    /// </summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="request">The version.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The integration.</returns>
    /// <response code="200">The integration, on the version.</response>
    /// <response code="400">Veyra does not ship the version.</response>
    /// <response code="404">No server has this ID.</response>
    [HttpPost("sqlserver/{id:guid}/version")]
    [Consumes("application/json")]
    [ProducesResponseType<IntegrationItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetVersionAsync(
        Guid id,
        VersionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await _servers.SetVersionAsync(id, request.Version, User, cancellationToken) is null)
        {
            return NotFound();
        }

        return await _integrations.FindAsync(id, cancellationToken) is { } item ? Ok(item) : NotFound();
    }

    /// <summary>
    /// Starts a probe of a SQL Server connection through an agent: Test connection before saving, or Validate now of a
    /// saved server. The agent picks it up with its next poll and answers within a minute or so; its Collector applies
    /// the probe's configuration, which pauses its other reads for a few seconds.
    /// </summary>
    /// <param name="input">The connection, unsaved, or the saved server to validate.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe's ID.</returns>
    /// <response code="202">
    /// The probe was started, or failed at once for want of an agent; read it to know which.
    /// </response>
    /// <response code="400">The connection breaks a rule; the title says which.</response>
    /// <response code="404">No server has the ID given as <c>serverId</c>.</response>
    [HttpPost("probes")]
    [Consumes("application/json")]
    [ProducesResponseType<ProbeStartedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartProbeAsync(ProbeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return await _probes.StartAsync(input, cancellationToken) is { } probe
            ? Accepted($"/api/v1/integrations/probes/{probe.Id}", new ProbeStartedResponse(probe.Id))
            : NotFound();
    }

    /// <summary>Reads a probe: pending, its result, or why it failed. Its outcome is kept for a day.</summary>
    /// <param name="id">The probe's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The probe.</returns>
    /// <response code="200">The probe.</response>
    /// <response code="404">No probe has this ID, or it is older than a day.</response>
    [HttpGet("probes/{id:guid}")]
    [ProducesResponseType<ProbeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProbeAsync(Guid id, CancellationToken cancellationToken) =>
        await _probes.GetAsync(id, cancellationToken) is { } probe
            ? Ok(IntegrationService.Probe(probe))
            : NotFound();
}
