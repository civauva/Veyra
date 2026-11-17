using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
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

    /// <summary>Creates the controller.</summary>
    /// <param name="server">Builds the Server tab.</param>
    /// <param name="jobs">Builds the Jobs tab.</param>
    /// <param name="events">Builds the Events tab.</param>
    /// <param name="availability">Builds the availability.</param>
    /// <param name="configuration">Builds the configuration and security posture.</param>
    /// <param name="permissions">Reads what the monitoring login lacks.</param>
    internal DatabaseServerController(
        ServerViewBuilder server,
        JobsViewBuilder jobs,
        EventsViewBuilder events,
        AvailabilityViewBuilder availability,
        ConfigurationViewBuilder configuration,
        PermissionsViewBuilder permissions)
    {
        _server = server;
        _jobs = jobs;
        _events = events;
        _availability = availability;
        _configuration = configuration;
        _permissions = permissions;
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
    /// <param name="range">The range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The events, the newest first.</returns>
    [HttpGet("events")]
    public Task<IReadOnlyList<EventResponse>> EventsAsync(
        string engine,
        string server,
        [FromQuery] string? range,
        CancellationToken cancellationToken) =>
        _events.EventsAsync(engine, server, range, cancellationToken);

    /// <summary>The server's SQL Server Agent jobs with their runs and steps.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The jobs, those that need a look first.</returns>
    [HttpGet("jobs")]
    public Task<IReadOnlyList<JobResponse>> JobsAsync(
        string engine,
        string server,
        CancellationToken cancellationToken) =>
        _jobs.JobsAsync(engine, server, cancellationToken);

    /// <summary>The server's Server tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The tab; 404 when the server sent nothing in the range.</returns>
    [HttpGet("server")]
    [ProducesResponseType<ServerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ServerAsync(
        string engine,
        string server,
        [FromQuery] string? range,
        CancellationToken cancellationToken) =>
        await _server.ServerAsync(engine, server, range, cancellationToken) is { } view ? Ok(view) : NotFound();
}
