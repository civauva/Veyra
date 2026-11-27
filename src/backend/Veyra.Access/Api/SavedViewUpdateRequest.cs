namespace Veyra.Access.Api;

/// <summary>New values for a saved view; its screen stays (ADR 0024).</summary>
/// <param name="Name">Its name, 1 to 80 characters.</param>
/// <param name="Query">The query text, at most 2,000 characters.</param>
/// <param name="Shared">Whether everyone who signs in sees it.</param>
public sealed record SavedViewUpdateRequest(string? Name, string? Query, bool Shared);
