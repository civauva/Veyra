using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Agents.Agents;
using Veyra.Agents.Enrollment;
using Veyra.Core.Security;

namespace Veyra.Agents.Api;

/// <summary>
/// Veyra's agents (ADR 0019): lists them, adds agents for other networks with a one-time code, and removes them.
/// Administrators only, like every page under Settings.
/// </summary>
[ApiController]
[Route("api/v1/agents")]
[Authorize(Policy = VeyraPolicies.Admin)]
[AgentsErrors]
public sealed class AgentsController : ControllerBase
{
    private readonly AgentsOverview _overview;

    private readonly AgentEnrollment _enrollment;

    private readonly AgentHostService _hosts;

    /// <summary>Creates the controller. The Agents module builds it, so its services can stay internal.</summary>
    /// <param name="overview">Reads the agents with their states.</param>
    /// <param name="enrollment">Adds, installs, and removes agents for other networks.</param>
    /// <param name="hosts">Sets what an agent reads of its own host.</param>
    internal AgentsController(AgentsOverview overview, AgentEnrollment enrollment, AgentHostService hosts)
    {
        _overview = overview;
        _enrollment = enrollment;
        _hosts = hosts;
    }

    /// <summary>Lists the active agents, the built-in one first.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The agents with their states.</returns>
    [HttpGet]
    public async Task<IReadOnlyList<AgentResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await _overview.ListAgentsAsync(cancellationToken)).Select(ToResponse).ToList();

    /// <summary>
    /// Adds an agent for a network Veyra cannot reach, with a one-time code that installs it. The code works once, for
    /// an hour, and is shown only in this answer.
    /// </summary>
    /// <param name="request">The agent's name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The agent, not installed yet, and its code.</returns>
    /// <response code="201">The agent was added.</response>
    /// <response code="400">The name is empty or too long.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<AddedAgentResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync(AddAgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var added = await _enrollment.AddAsync(request.Name, User, cancellationToken);
        var view = new AgentView(added.Agent, AgentState.NotConnected, 0, added.ExpiresAt);

        return Created(
            $"/api/v1/agents/{added.Agent.Id}",
            new AddedAgentResponse(ToResponse(view), added.Code, added.ExpiresAt));
    }

    /// <summary>
    /// Gives an agent for another network a new one-time code, to install it for the first time or again. Installing
    /// again gives it a new key and revokes the old one.
    /// </summary>
    /// <param name="id">The agent's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The code and when it stops working.</returns>
    /// <response code="200">The new code.</response>
    /// <response code="404">No agent in another network has this ID.</response>
    [HttpPost("{id:guid}/code")]
    [ProducesResponseType<IssuedCode>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> NewCodeAsync(Guid id, CancellationToken cancellationToken) =>
        await _enrollment.NewCodeAsync(id, User, cancellationToken) is { } code ? Ok(code) : NotFound();

    /// <summary>
    /// Sets what an installed agent reads of the machine it runs on: its CPU, memory, disks, and network, and on
    /// Windows also the Event Log's errors and IIS, on Linux also the Docker containers. The agent gets its new
    /// configuration on its next poll.
    /// </summary>
    /// <param name="id">The agent's ID.</param>
    /// <param name="request">What to read, and the environment the host belongs to.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The agent with its new setting.</returns>
    /// <response code="200">The setting was saved.</response>
    /// <response code="400">
    /// It is the built-in agent or not installed, the Event Log or IIS is asked for off Windows, Docker is asked for
    /// on Windows, or the environment is not a plain name.
    /// </response>
    /// <response code="404">No agent has this ID.</response>
    [HttpPut("{id:guid}/host")]
    [Consumes("application/json")]
    [ProducesResponseType<AgentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetHostAsync(
        Guid id,
        AgentHostRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var watch = new HostWatch(request.Host, request.EventLog, request.Iis, request.Environment, request.Docker);

        if (await _hosts.SetWatchAsync(id, watch, User, cancellationToken) is null)
        {
            return NotFound();
        }

        var view = (await _overview.ListAgentsAsync(cancellationToken)).Single(one => one.Agent.Id == id);

        return Ok(ToResponse(view));
    }

    /// <summary>
    /// Removes an agent for another network and revokes its key, so it can no longer connect or send. Uninstall it on
    /// its machine as well.
    /// </summary>
    /// <param name="id">The agent's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The agent was removed.</response>
    /// <response code="400">It is the built-in agent.</response>
    /// <response code="404">No agent has this ID.</response>
    /// <response code="409">It still reads database servers.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await _enrollment.RemoveAsync(id, User, cancellationToken) ? NoContent() : NotFound();

    private static AgentResponse ToResponse(AgentView view)
    {
        var report = view.Agent.Report;

        return new AgentResponse(
            Id: view.Agent.Id,
            Name: view.Agent.Name,
            Placement: view.Agent.Placement,
            State: view.State,
            Version: report.Version,
            HostName: report.HostName,
            OperatingSystem: report.OperatingSystem,
            LastSeenAt: report.LastSeenAt,
            Error: view.State switch
            {
                AgentState.ConfigurationFailed => report.ConfigError,
                AgentState.Unhealthy => report.HealthError,
                _ => null,
            },
            Servers: view.Servers,
            CreatedAt: view.Agent.CreatedAt)
        {
            Installed = view.Agent.IngestionKeyId is not null,
            CodeExpiresAt = view.CodeExpiresAt,
            Host = view.Agent.Watch.Host,
            EventLog = view.Agent.Watch.EventLog,
            Iis = view.Agent.Watch.Iis,
            Docker = view.Agent.Watch.Docker,
            HostEnvironment = view.Agent.Watch.Environment,
        };
    }
}
