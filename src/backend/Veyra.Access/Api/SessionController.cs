using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;
using Veyra.Access.Users;
using SignInResult = Veyra.Access.Users.SignInResult;

namespace Veyra.Access.Api;

/// <summary>
/// The signed-in session: sign in in one or two steps, sign out, who am I, and the antiforgery token (ADR 0008,
/// ADR 0011).
/// </summary>
[ApiController]
[Route("api/v1/session")]
[AccessErrors]
public sealed class SessionController : ControllerBase
{
    private const string _wrongCredentials =
        "The email or password is wrong, or the account cannot sign in right now.";

    private readonly SignInService _signIn;
    private readonly SessionService _sessions;
    private readonly SecondFactorService _secondFactor;
    private readonly UserService _users;
    private readonly UserStore _store;
    private readonly IAntiforgery _antiforgery;

    internal SessionController(
        SignInService signIn,
        SessionService sessions,
        SecondFactorService secondFactor,
        UserService users,
        UserStore store,
        IAntiforgery antiforgery)
    {
        _signIn = signIn;
        _sessions = sessions;
        _secondFactor = secondFactor;
        _users = users;
        _store = store;
        _antiforgery = antiforgery;
    }

    /// <summary>Signs in with a local email and password, and sets the session cookie.</summary>
    /// <remarks>
    /// Every failure gives the same answer, so the response never reveals whether an email exists or an account is
    /// locked. An account with a second factor, or one that must set one up, gets <c>202</c> and a short-lived
    /// cookie for the next step instead of a session.
    /// </remarks>
    /// <param name="request">The email and password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The signed-in user, or the next step.</returns>
    /// <response code="200">Signed in. Fetch an antiforgery token next.</response>
    /// <response code="202">The password was right; <c>next</c> says which step follows.</response>
    /// <response code="400">The request came over plain HTTP; signing in needs HTTPS.</response>
    /// <response code="401">The email or password is wrong, or the account cannot sign in right now.</response>
    /// <response code="429">Too many attempts from this address. Wait a minute.</response>
    [HttpPost]
    [AllowAnonymous]
    [RequireHttpsForSession]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting(AccessServiceCollectionExtensions.SignInRateLimitPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SignInNextResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _signIn.SignInAsync(
            request.Email,
            request.Password,
            SignInClient.From(HttpContext),
            cancellationToken);

        return result.Step switch
        {
            SignInStep.SignedIn => await SignedInAsync(result, cancellationToken),
            SignInStep.NeedsCode => await PendingAsync(result.User!, "code"),
            SignInStep.NeedsSetup => await PendingAsync(result.User!, "setup"),
            _ => Problem(title: _wrongCredentials, statusCode: StatusCodes.Status401Unauthorized),
        };
    }

    /// <summary>The second step: a code from the authenticator app, or a recovery code.</summary>
    /// <param name="request">The code.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The signed-in user.</returns>
    /// <response code="200">Signed in. Fetch an antiforgery token next.</response>
    /// <response code="401">The code is wrong or used, the step expired, or the account cannot sign in.</response>
    /// <response code="429">Too many attempts from this address. Wait a minute.</response>
    [HttpPost("second-factor")]
    [Authorize(AuthenticationSchemes = SessionPrincipal.PendingScheme)]
    [RequireHttpsForSession]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting(AccessServiceCollectionExtensions.SignInRateLimitPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SecondStepAsync(
        SecondFactorCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (User.PendingStep() != "code")
        {
            return Problem(title: "Sign in with your password first.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await _signIn.SecondStepAsync(
            User.UserId(),
            request.Code,
            SignInClient.From(HttpContext),
            cancellationToken);

        return result.Succeeded
            ? await SignedInAsync(result, cancellationToken)
            : Problem(title: "The code is wrong or was used already.", statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Starts setting up a second factor that an Admin requires, right after the password step.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The secret and its address, shown once.</returns>
    /// <response code="200">The secret.</response>
    /// <response code="401">The step expired, or this account does not have to set one up.</response>
    [HttpPost("second-factor/setup")]
    [Authorize(AuthenticationSchemes = SessionPrincipal.PendingScheme)]
    [RequireHttpsForSession]
    [IgnoreAntiforgeryToken]
    [ProducesResponseType<SecondFactorSetupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> StartRequiredSetupAsync(CancellationToken cancellationToken)
    {
        if (await PendingSetupUserAsync(cancellationToken) is not { } user
            || await _secondFactor.StartSetupAsync(user, cancellationToken) is not { } setup)
        {
            return Problem(title: "Sign in with your password first.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(new SecondFactorSetupResponse(setup.Secret, setup.Uri));
    }

    /// <summary>
    /// Confirms the required second factor with a working code, and starts the session.
    /// </summary>
    /// <param name="request">The code the app shows now.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The signed-in user and the recovery codes, shown once.</returns>
    /// <response code="200">Signed in, with the recovery codes.</response>
    /// <response code="400">The code is wrong.</response>
    /// <response code="401">The step expired, or this account does not have to set one up.</response>
    [HttpPost("second-factor/setup/confirm")]
    [Authorize(AuthenticationSchemes = SessionPrincipal.PendingScheme)]
    [RequireHttpsForSession]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting(AccessServiceCollectionExtensions.SignInRateLimitPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType<SetupConfirmedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ConfirmRequiredSetupAsync(
        SecondFactorCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await PendingSetupUserAsync(cancellationToken) is not { } user)
        {
            return Problem(title: "Sign in with your password first.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (await _secondFactor.ConfirmSetupAsync(user, request.Code, null, cancellationToken) is not { } codes)
        {
            return Problem(
                title: "The code is wrong. Try the one your app shows now.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await _signIn.FinishAfterSetupAsync(user, SignInClient.From(HttpContext), cancellationToken);
        await BeginSessionAsync(result, cancellationToken);

        return Ok(new SetupConfirmedResponse(AccessResponses.Session(result.User!), codes));
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

        return user is null ? Unauthorized() : Ok(AccessResponses.Session(user));
    }

    /// <summary>Signs out: ends the session and removes the session cookie.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Signed out.</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOutAsync(CancellationToken cancellationToken)
    {
        if (User.SessionId() is { } sessionId)
        {
            await _sessions.SignOutAsync(User.UserId(), sessionId, cancellationToken);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return NoContent();
    }

    /// <summary>
    /// Issues the antiforgery token for this session. Send it in the <c>X-Veyra-Antiforgery</c> header on every
    /// request that changes state.
    /// </summary>
    /// <returns>The token.</returns>
    /// <response code="200">The token.</response>
    /// <response code="400">The request came over plain HTTP; the antiforgery cookie needs HTTPS.</response>
    [HttpGet("antiforgery")]
    [RequireHttpsForSession]
    [ProducesResponseType<AntiforgeryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public AntiforgeryResponse GetAntiforgeryToken() =>
        new(_antiforgery.GetAndStoreTokens(HttpContext).RequestToken!);

    /// <summary>
    /// Changes the signed-in user's own password. Every other browser is signed out; this one stays.
    /// </summary>
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
            User.SessionId(),
            cancellationToken);

        return changed
            ? NoContent()
            : Problem(title: "The current password is wrong.", statusCode: StatusCodes.Status400BadRequest);
    }

    private async Task<IActionResult> SignedInAsync(SignInResult result, CancellationToken cancellationToken)
    {
        await BeginSessionAsync(result, cancellationToken);

        return Ok(AccessResponses.Session(result.User!));
    }

    // Records the session, replaces a pending cookie with the session cookie.
    private async Task BeginSessionAsync(SignInResult result, CancellationToken cancellationToken)
    {
        var user = result.User!;
        var session = await _sessions.StartAsync(
            user.Id,
            result.Method,
            SignInClient.From(HttpContext),
            cancellationToken);

        await HttpContext.SignOutAsync(SessionPrincipal.PendingScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SessionPrincipal.Create(user, session.Id));
    }

    private async Task<IActionResult> PendingAsync(AccessUser user, string step)
    {
        await HttpContext.SignInAsync(SessionPrincipal.PendingScheme, SessionPrincipal.CreatePending(user, step));

        return StatusCode(StatusCodes.Status202Accepted, new SignInNextResponse(step));
    }

    // The setup step is only for someone whose password was just right and who must set one up.
    private async Task<AccessUser?> PendingSetupUserAsync(CancellationToken cancellationToken)
    {
        if (User.PendingStep() != "setup")
        {
            return null;
        }

        var user = await _store.FindByIdAsync(User.UserId(), cancellationToken);

        return user is { Disabled: false } && await _secondFactor.IsRequiredAsync(user, cancellationToken)
            ? user
            : null;
    }
}
