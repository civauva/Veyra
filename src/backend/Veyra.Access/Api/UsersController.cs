using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;
using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>
/// Manages users: who can sign in, their role and team, their password, second factor, and sessions, and who must use
/// a second factor. Administrators only (ADR 0008, ADR 0011).
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = VeyraPolicies.Admin)]
[AccessErrors]
public sealed class UsersController : ControllerBase
{
    private readonly UserService _users;
    private readonly UserDirectory _directory;
    private readonly SecondFactorService _secondFactor;
    private readonly SessionService _sessions;

    internal UsersController(
        UserService users,
        UserDirectory directory,
        SecondFactorService secondFactor,
        SessionService sessions)
    {
        _users = users;
        _directory = directory;
        _secondFactor = secondFactor;
        _sessions = sessions;
    }

    /// <summary>Lists all users with what their badges show.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The users, ordered by email.</returns>
    [HttpGet]
    public async Task<IEnumerable<UserResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await _directory.ListAsync(cancellationToken)).Select(AccessResponses.User);

    /// <summary>Returns one user in full: sign-ins, sessions, and the history of the account.</summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The user.</returns>
    /// <response code="200">The user.</response>
    /// <response code="404">No user has this ID.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await _directory.DetailAsync(id, cancellationToken) is { } detail
            ? Ok(AccessResponses.Detail(detail))
            : NotFound();

    /// <summary>Creates a local user.</summary>
    /// <param name="request">The new user's details and first password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new user.</returns>
    /// <response code="201">The user was created.</response>
    /// <response code="400">The email, password, or team is not acceptable.</response>
    /// <response code="409">A user with this email already exists.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _users.CreateAsync(
            request.Email,
            request.DisplayName,
            request.Role,
            request.Password,
            request.Team,
            User.UserId(),
            cancellationToken);

        return Created($"/api/v1/users/{user.Id}", AccessResponses.User(new PersonSummary(user, null, 0, null)));
    }

    /// <summary>
    /// Changes a user's role, team, or whether they can sign in. Switching someone off ends their sessions at once.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="request">The fields to change.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The changed user.</returns>
    /// <response code="200">The user was changed.</response>
    /// <response code="400">The team is too long.</response>
    /// <response code="404">No user has this ID.</response>
    /// <response code="409">The change would leave Veyra without an active administrator.</response>
    [HttpPatch("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var updated = await _users.UpdateAsync(
            id,
            request.Role,
            request.Disabled,
            request.Team,
            User.UserId(),
            cancellationToken);

        if (updated is null)
        {
            return NotFound();
        }

        var person = (await _directory.ListAsync(cancellationToken)).First(one => one.User.Id == id);

        return Ok(AccessResponses.User(person));
    }

    /// <summary>
    /// Sets a new password for someone, for a forgotten password or a lock. It lifts the lock and signs them out
    /// everywhere.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="request">The new password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The password was reset.</response>
    /// <response code="400">The password is not acceptable.</response>
    /// <response code="404">No user has this ID.</response>
    [HttpPost("{id:guid}/password")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasswordAsync(
        Guid id,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _users.ResetPasswordAsync(id, request.Password, User.UserId(), cancellationToken)
            ? NoContent()
            : NotFound();
    }

    /// <summary>
    /// Removes someone's second factor, for a lost phone. They are signed out everywhere and, if it is required, set
    /// up a new one at their next sign-in.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The second factor was removed.</response>
    /// <response code="404">The user has no second factor switched on.</response>
    [HttpDelete("{id:guid}/second-factor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveSecondFactorAsync(Guid id, CancellationToken cancellationToken) =>
        await _secondFactor.RemoveAsync(id, User.UserId(), cancellationToken) ? NoContent() : NotFound();

    /// <summary>Ends one of someone's sessions.</summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The session ended.</response>
    /// <response code="404">The user has no open session with that ID.</response>
    [HttpDelete("{id:guid}/sessions/{sessionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EndSessionAsync(Guid id, string sessionId, CancellationToken cancellationToken) =>
        await _sessions.EndAsync(id, sessionId, User.UserId(), cancellationToken) ? NoContent() : NotFound();

    /// <summary>Signs someone out everywhere; the password stays.</summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many sessions ended.</returns>
    [HttpDelete("{id:guid}/sessions")]
    public async Task<EndedSessionsResponse> EndSessionsAsync(Guid id, CancellationToken cancellationToken) =>
        new(await _sessions.EndAllAsync(id, null, User.UserId(), cancellationToken));

    /// <summary>Returns who must sign in with a second factor.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The policy.</returns>
    [HttpGet("second-factor-policy")]
    public async Task<SecondFactorPolicyResponse> GetPolicyAsync(CancellationToken cancellationToken) =>
        new(await _secondFactor.GetPolicyAsync(cancellationToken));

    /// <summary>
    /// Sets who must sign in with a second factor. People it newly covers set one up at their next sign-in.
    /// </summary>
    /// <param name="request">The policy.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored policy.</returns>
    /// <response code="200">The policy.</response>
    /// <response code="400">The value is not Nobody, Admins, or Everyone.</response>
    [HttpPut("second-factor-policy")]
    [Consumes("application/json")]
    [ProducesResponseType<SecondFactorPolicyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPolicyAsync(
        SecondFactorPolicyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.RequiredFor))
        {
            return Problem(title: "Choose Nobody, Admins, or Everyone.", statusCode: StatusCodes.Status400BadRequest);
        }

        await _secondFactor.SetPolicyAsync(request.RequiredFor, User.UserId(), cancellationToken);

        return Ok(new SecondFactorPolicyResponse(request.RequiredFor));
    }
}
