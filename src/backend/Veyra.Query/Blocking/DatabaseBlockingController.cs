using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Blocking.Api;

namespace Veyra.Query.Blocking;

/// <summary>A database server's blocking episodes and deadlocks over a range (ADR 0020, area 5).</summary>
[ApiController]
[Route("api/v1/metrics/databases/{engine}/{server}")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class DatabaseBlockingController : ControllerBase
{
    private readonly BlockingViewBuilder _blocking;

    /// <summary>Creates the controller.</summary>
    /// <param name="blocking">Builds the Blocking tab.</param>
    internal DatabaseBlockingController(BlockingViewBuilder blocking)
    {
        _blocking = blocking;
    }

    /// <summary>The server's Blocking tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The range, such as <c>24h</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The episodes and deadlocks.</returns>
    [HttpGet("blocking")]
    public Task<BlockingResponse> BlockingAsync(
        string engine,
        string server,
        [FromQuery] string? range,
        CancellationToken cancellationToken) =>
        _blocking.BlockingAsync(engine, server, range, cancellationToken);
}
