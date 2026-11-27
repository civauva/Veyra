namespace Veyra.Access.Api;

/// <summary>A saved view, as <c>/api/v1/views</c> answers it.</summary>
/// <param name="Id">The view's ID.</param>
/// <param name="Screen">The screen it belongs to.</param>
/// <param name="Name">Its name.</param>
/// <param name="Query">The query text.</param>
/// <param name="Shared">Whether everyone sees it.</param>
/// <param name="Mine">Whether the signed-in person owns it, and so may change and delete it.</param>
/// <param name="OwnerName">The display name of the person who keeps it, so a shared view says who shared it.</param>
/// <param name="CreatedAt">When it was saved first.</param>
/// <param name="UpdatedAt">When it was changed last.</param>
public sealed record SavedViewResponse(
    Guid Id,
    string Screen,
    string Name,
    string Query,
    bool Shared,
    bool Mine,
    string OwnerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
