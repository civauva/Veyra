using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Access.Sessions;
using Veyra.Access.Views;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>
/// Saved views (ADR 0024): each signed-in person keeps named queries of a screen, and may share them with everyone.
/// Only the owner changes or deletes a view.
/// </summary>
[ApiController]
[Route("api/v1/views")]
[Authorize(Policy = VeyraPolicies.Viewer)]
[AccessErrors]
public sealed class ViewsController : ControllerBase
{
    private readonly SavedViewService _views;

    internal ViewsController(SavedViewService views)
    {
        _views = views;
    }

    /// <summary>Lists the views of a screen the person sees: their own first, then those others shared.</summary>
    /// <param name="screen">The screen: <c>logs</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The views.</returns>
    /// <response code="200">The views, possibly none.</response>
    /// <response code="400">The screen has no saved views.</response>
    [HttpGet]
    [ProducesResponseType<SavedViewListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<SavedViewListResponse> ListAsync(
        [FromQuery] string? screen,
        CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var views = await _views.ListAsync(userId, screen, cancellationToken);

        return new SavedViewListResponse([.. views.Select(view => ToResponse(view, userId))]);
    }

    /// <summary>Saves a new view of the person's.</summary>
    /// <param name="request">The view.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The saved view.</returns>
    /// <response code="201">The view is saved.</response>
    /// <response code="400">The screen, name, or query is not acceptable.</response>
    /// <response code="409">The person keeps 100 views already.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<SavedViewResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        SavedViewCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = User.UserId();
        var view = await _views.CreateAsync(
            userId,
            request.Screen,
            request.Name,
            request.Query,
            request.Shared,
            cancellationToken);

        return Created($"/api/v1/views/{view.Id}", ToResponse(view, userId));
    }

    /// <summary>Changes the name, query, and sharing of one of the person's views.</summary>
    /// <param name="id">The view's ID.</param>
    /// <param name="request">The new values.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The changed view.</returns>
    /// <response code="200">The view is changed.</response>
    /// <response code="400">The name or query is not acceptable.</response>
    /// <response code="403">The view is shared by someone else.</response>
    /// <response code="404">The person sees no view with that ID.</response>
    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<SavedViewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        SavedViewUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = User.UserId();
        var (change, view) = await _views.UpdateAsync(
            userId,
            id,
            request.Name,
            request.Query,
            request.Shared,
            cancellationToken);

        return change == SavedViewChange.Done ? Ok(ToResponse(view!, userId)) : Refused(change);
    }

    /// <summary>Deletes one of the person's views.</summary>
    /// <param name="id">The view's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The view is deleted.</response>
    /// <response code="403">The view is shared by someone else.</response>
    /// <response code="404">The person sees no view with that ID.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var change = await _views.DeleteAsync(User.UserId(), id, cancellationToken);

        return change == SavedViewChange.Done ? NoContent() : Refused(change);
    }

    private static SavedViewResponse ToResponse(SavedView view, Guid userId) => new(
        view.Id,
        view.Screen,
        view.Name,
        view.Query,
        view.Shared,
        view.OwnerId == userId,
        view.OwnerName,
        view.CreatedAt,
        view.UpdatedAt);

    private ObjectResult Refused(SavedViewChange change) => change == SavedViewChange.NotOwner
        ? Problem(
            title: "Only the person who saved this view may change or delete it.",
            statusCode: StatusCodes.Status403Forbidden)
        : Problem(title: "There is no such view.", statusCode: StatusCodes.Status404NotFound);
}
