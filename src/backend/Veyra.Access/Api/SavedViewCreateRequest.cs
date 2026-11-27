namespace Veyra.Access.Api;

/// <summary>A new saved view (ADR 0024).</summary>
/// <param name="Screen">The screen it belongs to: <c>logs</c>.</param>
/// <param name="Name">Its name, 1 to 80 characters.</param>
/// <param name="Query">The query text, at most 2,000 characters; empty or missing for all records.</param>
/// <param name="Shared">Whether everyone who signs in sees it.</param>
public sealed record SavedViewCreateRequest(string? Screen, string? Name, string? Query, bool Shared);
