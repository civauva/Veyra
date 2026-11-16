using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;

namespace Veyra.Query.Home;

/// <summary>
/// Serves the home snapshot that every home dashboard draws (ADR 0007). The frontend polls it every 15 seconds; the
/// snapshot of an environment is built once every 5 seconds and shared by everyone who asks (ADR 0015).
/// </summary>
[ApiController]
[Route("api/v1/home")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class HomeController : ControllerBase
{
    private readonly SharedHomeSnapshots _homes;

    /// <summary>Creates the controller. The Query module builds it, so its snapshots can stay internal.</summary>
    /// <param name="homes">The shared snapshots.</param>
    internal HomeController(SharedHomeSnapshots homes)
    {
        _homes = homes;
    }

    /// <summary>Returns the home snapshot of one environment, or of all environments together.</summary>
    /// <param name="environment">
    /// The environment to show, for example <c>production</c>. Leave it out for all environments, including records
    /// that name none.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The snapshot.</returns>
    /// <response code="200">The snapshot.</response>
    [HttpGet]
    [ProducesResponseType<HomeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] string? environment, CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(environment) ? null : environment;

        return Ok(await _homes.GetAsync(scope, cancellationToken));
    }
}
