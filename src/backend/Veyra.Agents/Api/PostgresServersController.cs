using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Agents.PostgresServers;
using Veyra.Core.Security;

namespace Veyra.Agents.Api;

/// <summary>
/// Manages the PostgreSQL servers Veyra's agents read (VEY-33, ADR 0019), as the SQL Server API does. Administrators
/// only, because a server comes with a role and its password. Passwords go in and never come back.
/// </summary>
[ApiController]
[Route("api/v1/postgres-servers")]
[Authorize(Policy = VeyraPolicies.Admin)]
[AgentsErrors]
public sealed class PostgresServersController : ControllerBase
{
    private readonly PostgresServerService _servers;

    private readonly AgentsOverview _overview;

    /// <summary>Creates the controller. The Agents module builds it, so its services can stay internal.</summary>
    /// <param name="servers">Adds, changes, and removes servers.</param>
    /// <param name="overview">Reads the servers with their states.</param>
    internal PostgresServersController(PostgresServerService servers, AgentsOverview overview)
    {
        _servers = servers;
        _overview = overview;
    }

    /// <summary>Lists every server with its state.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The servers, by name.</returns>
    [HttpGet]
    public async Task<IReadOnlyList<PostgresServerResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await _overview.ListPostgresServersAsync(cancellationToken)).Select(ToResponse).ToList();

    /// <summary>Adds a server. Its agent starts reading it with its next poll, within 30 seconds.</summary>
    /// <param name="input">The server with its role's password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored server.</returns>
    /// <response code="201">The server was added.</response>
    /// <response code="400">The input breaks a rule; the title says which.</response>
    /// <response code="409">Another PostgreSQL server has the name.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<PostgresServerResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync(PostgresServerInput input, CancellationToken cancellationToken)
    {
        var server = await _servers.AddAsync(input, User, cancellationToken);

        return Created($"/api/v1/postgres-servers/{server.Id}", await FindAsync(server.Id, cancellationToken));
    }

    /// <summary>Changes a server. Leave the password out to keep the stored one.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="input">The server as it should be.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored server.</returns>
    /// <response code="200">The server was changed.</response>
    /// <response code="404">No server has this ID.</response>
    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<PostgresServerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        PostgresServerInput input,
        CancellationToken cancellationToken) =>
        await _servers.UpdateAsync(id, input, User, cancellationToken) is { } server
            ? Ok(await FindAsync(server.Id, cancellationToken))
            : NotFound();

    /// <summary>
    /// Removes a server. Its agent stops reading it; the data it sent stays until retention removes it.
    /// </summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The server was removed.</response>
    /// <response code="404">No server has this ID.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await _servers.RemoveAsync(id, User, cancellationToken) ? NoContent() : NotFound();

    private async Task<PostgresServerResponse?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await _overview.ListPostgresServersAsync(cancellationToken))
            .Where(view => view.Server.Id == id)
            .Select(ToResponse)
            .SingleOrDefault();

    private static PostgresServerResponse ToResponse(PostgresServerView view) =>
        new(
            Id: view.Server.Id,
            Name: view.Server.Name,
            Address: view.Server.Address,
            Port: view.Server.Port,
            Environment: view.Server.Environment,
            AgentId: view.Server.AgentId,
            AgentName: view.AgentName,
            Login: view.Server.Login,
            Encryption: view.Server.Encryption,
            State: view.Status.State,
            Detail: view.Status.Detail,
            Since: view.Status.Since,
            CreatedAt: view.Server.CreatedAt,
            ChangedAt: view.Server.ChangedAt);
}
