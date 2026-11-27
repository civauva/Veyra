using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Storage.Api;

namespace Veyra.Query.Storage;

/// <summary>A database server's databases and volumes in full (ADR 0020, areas 6 to 8).</summary>
[ApiController]
[Route("api/v1/metrics/databases/{engine}/{server}")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class DatabaseStorageController : ControllerBase
{
    private readonly StorageViewBuilder _storage;

    private readonly IndexViewBuilder _indexes;

    /// <summary>Creates the controller.</summary>
    /// <param name="storage">Builds the databases and volumes.</param>
    /// <param name="indexes">Builds the index advice.</param>
    internal DatabaseStorageController(StorageViewBuilder storage, IndexViewBuilder indexes)
    {
        _storage = storage;
        _indexes = indexes;
    }

    /// <summary>The server's index and statistics advice.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="system">
    /// Whether the server's own databases are listed and advised on; left out when omitted.
    /// </param>
    /// <param name="simple">Whether to keep to the Simple view: the five worst findings and no list (VEY-67).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The advice.</returns>
    [HttpGet("indexes")]
    public Task<IndexesResponse> IndexesAsync(
        string engine,
        string server,
        [FromQuery] bool system,
        [FromQuery] bool simple,
        CancellationToken cancellationToken) =>
        _indexes.IndexesAsync(engine, server, system, simple, cancellationToken);

    /// <summary>The server's databases and volumes.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="system">Whether the server's own databases are listed; left out when omitted.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The databases and volumes; 404 when the server sent no storage read.</returns>
    [HttpGet("storage")]
    [ProducesResponseType<StorageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StorageAsync(
        string engine,
        string server,
        [FromQuery] bool system,
        CancellationToken cancellationToken) =>
        await _storage.StorageAsync(engine, server, system, cancellationToken) is { } view ? Ok(view) : NotFound();
}
