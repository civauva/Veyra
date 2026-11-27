using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Access.About;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>
/// Which Veyra answers and what each release brought (VEY-82): the version the app shows, the build an open page
/// compares with its own to offer a reload, and the release notes. Everyone signed in may read them; the version is
/// not told to anyone who is not.
/// </summary>
[ApiController]
[Route("api/v1/about")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class AboutController : ControllerBase
{
    private readonly BuildIdentity _build;

    /// <summary>Creates the controller.</summary>
    /// <param name="build">The build of the app this host serves.</param>
    internal AboutController(BuildIdentity build)
    {
        _build = build;
    }

    /// <summary>The version and the build that answer.</summary>
    /// <returns>The version, such as 0.4.1, and the build.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<AboutResponse> Get()
    {
        return new AboutResponse(VeyraVersion.Current, _build.Value);
    }

    /// <summary>The release notes, newest first; the first is the running version.</summary>
    /// <returns>Each release with its date and its changes by kind.</returns>
    [HttpGet("releases")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ReleaseResponse>> Releases()
    {
        return Ok(ReleaseNotes.Embedded);
    }
}
