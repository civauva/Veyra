using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Veyra.Access.Setup;

namespace Veyra.Access.Api;

/// <summary>
/// First-run setup: creates the first administrator with the token from the link in Veyra's log (ADR 0008).
/// </summary>
[ApiController]
[Route("api/v1/setup")]
[AllowAnonymous]
[AccessErrors]
public sealed class SetupController : ControllerBase
{
    private readonly SetupService _setup;

    internal SetupController(SetupService setup)
    {
        _setup = setup;
    }

    /// <summary>Tells the UI whether to show the setup page.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Whether setup is still needed.</returns>
    [HttpGet]
    public async Task<SetupStatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
        new(await _setup.IsRequiredAsync(cancellationToken));

    /// <summary>Creates the first administrator.</summary>
    /// <remarks>
    /// No antiforgery token is possible before anyone has signed in. The endpoint accepts only a JSON body, which a
    /// cross-site form cannot send, and it is rate-limited.
    /// </remarks>
    /// <param name="request">The token and the administrator's details.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// <c>201</c> when created, <c>400</c> for a wrong or expired token, <c>409</c> when users exist.
    /// </returns>
    /// <response code="201">The administrator was created. Sign in next.</response>
    /// <response code="400">
    /// The token is wrong, expired, or used, or the email or password is not acceptable.
    /// </response>
    /// <response code="409">Setup was already completed.</response>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting(AccessServiceCollectionExtensions.SignInRateLimitPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CompleteAsync(SetupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _setup.CompleteAsync(
            request.Token,
            request.Email,
            request.DisplayName,
            request.Password,
            cancellationToken);

        return result switch
        {
            SetupResult.Created => StatusCode(StatusCodes.Status201Created),
            SetupResult.AlreadyCompleted => Problem(
                title: "Setup was already completed.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Problem(
                title: "The setup link is wrong, expired, or already used. Restart Veyra for a new one.",
                statusCode: StatusCodes.Status400BadRequest),
        };
    }
}
