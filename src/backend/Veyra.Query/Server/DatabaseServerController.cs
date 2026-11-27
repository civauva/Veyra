using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Metrics;
using Veyra.Query.Server.Api;

namespace Veyra.Query.Server;

/// <summary>A database server as a whole: version, CPU, memory, waits, and the usual workload (ADR 0020).</summary>
[ApiController]
[Route("api/v1/metrics/databases/{engine}/{server}")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class DatabaseServerController : ControllerBase
{
    private readonly ServerViewBuilder _server;

    private readonly JobsViewBuilder _jobs;

    private readonly EventsViewBuilder _events;

    private readonly AvailabilityViewBuilder _availability;

    private readonly ConfigurationViewBuilder _configuration;

    private readonly PermissionsViewBuilder _permissions;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller.</summary>
    /// <param name="server">Builds the Server tab.</param>
    /// <param name="jobs">Builds the Jobs tab.</param>
    /// <param name="events">Builds the Events tab.</param>
    /// <param name="availability">Builds the availability.</param>
    /// <param name="configuration">Builds the configuration and security posture.</param>
    /// <param name="permissions">Reads what the monitoring login lacks.</param>
    /// <param name="clock">The clock that decides what now is, for a period that ends now.</param>
    internal DatabaseServerController(
        ServerViewBuilder server,
        JobsViewBuilder jobs,
        EventsViewBuilder events,
        AvailabilityViewBuilder availability,
        ConfigurationViewBuilder configuration,
        PermissionsViewBuilder permissions,
        TimeProvider clock)
    {
        _server = server;
        _jobs = jobs;
        _events = events;
        _availability = availability;
        _configuration = configuration;
        _permissions = permissions;
        _clock = clock;
    }

    /// <summary>
    /// The permissions the monitoring login lacks, the tabs they leave empty, and the script to grant them.
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The gaps; none when the login may read everything.</returns>
    [HttpGet("permissions")]
    public Task<PermissionsResponse> PermissionsAsync(
        string engine,
        string server,
        CancellationToken cancellationToken) =>
        _permissions.PermissionsAsync(engine, server, cancellationToken);

    /// <summary>The server's settings, security posture, and their changes.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The configuration.</returns>
    [HttpGet("configuration")]
    public Task<ConfigurationResponse> ConfigurationAsync(
        string engine,
        string server,
        CancellationToken cancellationToken) =>
        _configuration.ConfigurationAsync(engine, server, cancellationToken);

    /// <summary>The server's Availability Groups, log shipping, and cluster.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The availability; empty lists when the server has none.</returns>
    [HttpGet("availability")]
    public Task<AvailabilityResponse> AvailabilityAsync(
        string engine,
        string server,
        CancellationToken cancellationToken) =>
        _availability.AvailabilityAsync(engine, server, cancellationToken);

    /// <summary>The server's events from system_health over the range.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The range, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range (ADR 0022).</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="simple">Whether to leave out the events that need nothing, as in the Simple view (VEY-67).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The events, the newest first.</returns>
    /// <response code="200">The events.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    [HttpGet("events")]
    [ProducesResponseType<IReadOnlyList<EventResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EventsAsync(
        string engine,
        string server,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool simple,
        CancellationToken cancellationToken)
    {
        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        return Ok(await _events.EventsAsync(engine, server, span, simple, cancellationToken));
    }

    /// <summary>The server's SQL Server Agent jobs with their runs and steps.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="system">Whether the jobs SQL Server set up itself are listed; left out when omitted.</param>
    /// <param name="simple">Whether only the jobs that need a look are listed, as in the Simple view (VEY-67).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The jobs, those that need a look first.</returns>
    [HttpGet("jobs")]
    public Task<JobsResponse> JobsAsync(
        string engine,
        string server,
        [FromQuery] bool system,
        [FromQuery] bool simple,
        CancellationToken cancellationToken) =>
        _jobs.JobsAsync(engine, server, system, simple, cancellationToken);

    /// <summary>The server's Server tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The range, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range (ADR 0022).</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="system">Whether the idle waits of its background tasks are listed; left out when omitted.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The tab; 404 when the server sent nothing in the range.</returns>
    [HttpGet("server")]
    [ProducesResponseType<ServerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ServerAsync(
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

        return await _server.ServerAsync(engine, server, span, system, cancellationToken) is { } view
            ? Ok(view)
            : NotFound();
    }
}
