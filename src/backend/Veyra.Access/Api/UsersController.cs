using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>
/// Manages users. Administrators only (ADR 0008).
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = VeyraPolicies.Admin)]
[AccessErrors]
public sealed class UsersController : ControllerBase
{
    private readonly UserService _users;

    internal UsersController(UserService users)
    {
        _users = users;
    }

    /// <summary>Lists all users.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The users, ordered by email.</returns>
    [HttpGet]
    public async Task<IEnumerable<UserResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await _users.ListAsync(cancellationToken)).Select(ToResponse);

    /// <summary>Creates a local user.</summary>
    /// <param name="request">The new user's details and initial password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new user.</returns>
    /// <response code="201">The user was created.</response>
    /// <response code="400">The email or password is not acceptable.</response>
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
            cancellationToken);

        return Created($"/api/v1/users/{user.Id}", ToResponse(user));
    }

    /// <summary>
    /// Changes a user's role or disabled flag. A disabled user loses their session on the next request.
    /// </summary>
    /// <param name="id">The user's ID.</param>
    /// <param name="request">The fields to change.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The changed user.</returns>
    /// <response code="200">The user was changed.</response>
    /// <response code="404">No user has this ID.</response>
    /// <response code="409">The change would leave Veyra without an active administrator.</response>
    [HttpPatch("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _users.UpdateAsync(id, request.Role, request.Disabled, cancellationToken);

        return user is null ? NotFound() : Ok(ToResponse(user));
    }

    private static UserResponse ToResponse(AccessUser user) =>
        new(user.Id, user.Email, user.DisplayName, user.Role, user.Disabled, user.CreatedAt, user.LastSignInAt);
}
