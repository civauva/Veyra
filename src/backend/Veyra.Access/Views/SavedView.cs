namespace Veyra.Access.Views;

/// <summary>A named query of a screen that a person keeps, and may share (ADR 0024).</summary>
/// <param name="Id">The view's ID.</param>
/// <param name="OwnerId">The person who keeps it; only they change or delete it.</param>
/// <param name="OwnerName">The owner's display name, so a shared view says who shared it.</param>
/// <param name="Screen">The screen it belongs to, one of <see cref="SavedViewScreens.All"/>.</param>
/// <param name="Name">The name the person gave it.</param>
/// <param name="Query">The query text, as the screen's bar shows it. Never the time range.</param>
/// <param name="Shared">Whether everyone who signs in sees it.</param>
/// <param name="CreatedAt">When it was saved first.</param>
/// <param name="UpdatedAt">When it was changed last.</param>
internal sealed record SavedView(
    Guid Id,
    Guid OwnerId,
    string OwnerName,
    string Screen,
    string Name,
    string Query,
    bool Shared,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
