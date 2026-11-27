using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;
using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>
/// The signed-in person's own account: what Veyra knows about it, the second factor, and where it is signed in
/// (ADR 0011). Changing the password stays at <c>PUT /api/v1/session/password</c>.
/// </summary>
[ApiController]
[Route("api/v1/account")]
[Authorize(Policy = VeyraPolicies.Viewer)]
[AccessErrors]
public sealed class AccountController : ControllerBase
{
    private const int _signInsShown = 20;

    private readonly UserDirectory _directory;
    private readonly UserService _users;
    private readonly SecondFactorService _secondFactor;
    private readonly SessionService _sessions;

    internal AccountController(
        UserDirectory directory,
        UserService users,
        SecondFactorService secondFactor,
        SessionService sessions)
    {
        _directory = directory;
        _users = users;
        _secondFactor = secondFactor;
        _sessions = sessions;
    }

    /// <summary>Returns the account, its second factor, its sessions, and its latest sign-ins.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The account.</returns>
    /// <response code="200">The account.</response>
    [HttpGet]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (await _directory.DetailAsync(User.UserId(), cancellationToken) is not { } detail)
        {
            return Unauthorized();
        }

        var user = detail.User;

        return Ok(new AccountResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role,
            user.Team,
            user.CreatedAt,
            user.LastSignInAt,
            user.PasswordHash is not null,
            AccessResponses.SecondFactor(detail.SecondFactor),
            detail.SecondFactorRequired,
            [.. detail.Sessions.Select(one => AccessResponses.SessionInfo(one, User.SessionId()))],
            [.. detail.SignIns.Take(_signInsShown).Select(AccessResponses.SignIn)],
            user.TimeZone));
    }

    /// <summary>
    /// Sets the time zone every screen shows the person's times in (VEY-22), or clears it so the browser's is used.
    /// </summary>
    /// <param name="request">The zone.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The zone is kept.</response>
    /// <response code="400">The name is not an IANA time zone.</response>
    [HttpPut("time-zone")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetTimeZoneAsync(TimeZoneRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _users.SetTimeZoneAsync(User.UserId(), request.TimeZone, cancellationToken)
            ? NoContent()
            : Problem(
                title: "The time zone must be an IANA name, such as Europe/Berlin.",
                statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Sets the look of the app the person sees (ADR 0021), or clears it so the default look is used. Refused while an
    /// Admin has set one look for everyone.
    /// </summary>
    /// <param name="request">The look.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The look is kept.</response>
    /// <response code="400">The value is not one of the looks.</response>
    /// <response code="403">An Admin has set one look for everyone.</response>
    [HttpPut("look")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetLookAsync(LookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _users.SetLookAsync(User.UserId(), request.Look, cancellationToken) switch
        {
            LookChange.Kept => NoContent(),
            LookChange.UnknownLook => Problem(title: Looks.Unknown, statusCode: StatusCodes.Status400BadRequest),
            LookChange.NotAllowed => Problem(
                title: "An Admin has set one look for everyone.",
                statusCode: StatusCodes.Status403Forbidden),
            _ => Unauthorized(),
        };
    }

    /// <summary>Starts setting up a second factor. It is not asked for until a code confirms it.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The secret and its address, shown once.</returns>
    /// <response code="200">The secret.</response>
    /// <response code="409">A second factor is already on.</response>
    [HttpPost("second-factor")]
    [ProducesResponseType<SecondFactorSetupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartSetupAsync(CancellationToken cancellationToken)
    {
        if (await CurrentUserAsync(cancellationToken) is not { } user)
        {
            return Unauthorized();
        }

        return await _secondFactor.StartSetupAsync(user, cancellationToken) is { } setup
            ? Ok(new SecondFactorSetupResponse(setup.Secret, setup.Uri))
            : Problem(title: "Two-factor sign-in is already on.", statusCode: StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Switches the second factor on with a working code. Every other browser is signed out; this one stays.
    /// </summary>
    /// <param name="request">The code the app shows now.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The recovery codes, shown once.</returns>
    /// <response code="200">It is on, with the recovery codes.</response>
    /// <response code="400">The code is wrong, or no setup was started.</response>
    [HttpPost("second-factor/confirm")]
    [Consumes("application/json")]
    [ProducesResponseType<RecoveryCodesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmSetupAsync(
        SecondFactorCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await CurrentUserAsync(cancellationToken) is not { } user)
        {
            return Unauthorized();
        }

        var codes = await _secondFactor.ConfirmSetupAsync(
            user,
            request.Code,
            User.SessionId(),
            cancellationToken);

        return codes is not null
            ? Ok(new RecoveryCodesResponse(codes))
            : Problem(
                title: "The code is wrong. Try the one your app shows now.",
                statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>Replaces the recovery codes with ten new ones. Needs the password again.</summary>
    /// <param name="request">The current password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new codes, shown once.</returns>
    /// <response code="200">The new codes.</response>
    /// <response code="400">The password is wrong.</response>
    /// <response code="409">Two-factor sign-in is not on.</response>
    [HttpPost("second-factor/recovery-codes")]
    [Consumes("application/json")]
    [ProducesResponseType<RecoveryCodesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplaceRecoveryCodesAsync(
        PasswordConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await CurrentUserAsync(cancellationToken) is not { } user)
        {
            return Unauthorized();
        }

        if (!_users.PasswordMatches(user, request.Password))
        {
            return WrongPassword();
        }

        var codes = await _secondFactor.ReplaceRecoveryCodesAsync(user, User.SessionId(), cancellationToken);

        return Ok(new RecoveryCodesResponse(codes));
    }

    /// <summary>Switches the second factor off. Needs the password again; refused when an Admin requires it.</summary>
    /// <param name="request">The current password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">It is off.</response>
    /// <response code="400">The password is wrong.</response>
    /// <response code="409">An Admin requires a second factor of this account.</response>
    [HttpPost("second-factor/off")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SwitchOffAsync(
        PasswordConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await CurrentUserAsync(cancellationToken) is not { } user)
        {
            return Unauthorized();
        }

        if (!_users.PasswordMatches(user, request.Password))
        {
            return WrongPassword();
        }

        await _secondFactor.SwitchOffAsync(user, User.SessionId(), cancellationToken);

        return NoContent();
    }

    /// <summary>Ends one of the person's own sessions, for example on a lost phone.</summary>
    /// <param name="id">The session ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The session ended.</response>
    /// <response code="404">No open session of this person has that ID.</response>
    [HttpDelete("sessions/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EndSessionAsync(string id, CancellationToken cancellationToken) =>
        await _sessions.EndAsync(User.UserId(), id, User.UserId(), cancellationToken) ? NoContent() : NotFound();

    /// <summary>Ends every other session of the person; this one stays.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many sessions ended.</returns>
    /// <response code="200">The number of ended sessions.</response>
    [HttpPost("sessions/end-others")]
    [ProducesResponseType<EndedSessionsResponse>(StatusCodes.Status200OK)]
    public async Task<EndedSessionsResponse> EndOtherSessionsAsync(CancellationToken cancellationToken) =>
        new(await _sessions.EndAllAsync(User.UserId(), User.SessionId(), User.UserId(), cancellationToken));

    private async Task<AccessUser?> CurrentUserAsync(CancellationToken cancellationToken) =>
        (await _directory.DetailAsync(User.UserId(), cancellationToken))?.User;

    private ObjectResult WrongPassword() =>
        Problem(title: "The password is wrong.", statusCode: StatusCodes.Status400BadRequest);
}
