using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Veyra.Access.Sessions;
using Veyra.Access.Users;

namespace Veyra.Access.Api;

/// <summary>
/// The signed-in session: sign in, sign out, who am I, and the antiforgery token (ADR 0008).
/// </summary>
[ApiController]
[Route("api/v1/session")]
[AccessErrors]
public sealed class SessionController : ControllerBase
{
    private readonly SignInService _signIn;
    private readonly UserService _users;
    private readonly UserStore _store;
    private readonly IAntiforgery _antiforgery;

    internal SessionController(SignInService signIn, UserService users, UserStore store, IAntiforgery antiforgery)
    {
        _signIn = signIn;
        _users = users;
        _store = store;
        _antiforgery = antiforgery;
    }

    /// <summary>Signs in with a local email and password, and sets the session cookie.</summary>
    /// <remarks>
    /// Every failure gives the same answer, so the response never reveals whether an email exists or an account is
    /// locked. The reason goes to Veyra's security log.
    /// </remarks>
    /// <param name="request">The email and password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The signed-in user.</returns>
    /// <response code="200">Signed in. Fetch an antiforgery token next.</response>
    /// <response code="401">The email or password is wrong, or the account cannot sign in right now.</response>
    /// <response code="429">Too many attempts from this address. Wait a minute.</response>
    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting(AccessServiceCollectionExtensions.SignInRateLimitPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _signIn.SignInAsync(request.Email, request.Password, cancellationToken);

        if (!result.Succeeded || result.User is null)
        {
            return Problem(
                title: "The email or password is wrong, or the account cannot sign in right now.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SessionPrincipal.Create(result.User));

        return Ok(ToResponse(result.User));
    }

    /// <summary>Returns the signed-in user.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The signed-in user.</returns>
    /// <response code="200">The user.</response>
    /// <response code="401">Not signed in.</response>
    [HttpGet]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var user = await _store.FindByIdAsync(User.UserId(), cancellationToken);

        return user is null ? Unauthorized() : Ok(ToResponse(user));
    }

    /// <summary>Signs out and removes the session cookie.</summary>
    /// <returns>No content.</returns>
    /// <response code="204">Signed out.</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return NoContent();
    }

    /// <summary>
    /// Issues the antiforgery token for this session. Send it in the <c>X-Veyra-Antiforgery</c> header on every
    /// request that changes state.
    /// </summary>
    /// <returns>The token.</returns>
    /// <response code="200">The token.</response>
    [HttpGet("antiforgery")]
    [ProducesResponseType<AntiforgeryResponse>(StatusCodes.Status200OK)]
    public AntiforgeryResponse GetAntiforgeryToken() =>
        new(_antiforgery.GetAndStoreTokens(HttpContext).RequestToken!);

    /// <summary>Changes the signed-in user's own password.</summary>
    /// <param name="request">The current and the new password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The password was changed.</response>
    /// <response code="400">The current password is wrong, or the new one is not acceptable.</response>
    [HttpPut("password")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var changed = await _users.ChangePasswordAsync(
            User.UserId(),
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        return changed
            ? NoContent()
            : Problem(title: "The current password is wrong.", statusCode: StatusCodes.Status400BadRequest);
    }

    private static SessionResponse ToResponse(AccessUser user) => new(user.Id, user.Email, user.DisplayName, user.Role);
}
