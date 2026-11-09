namespace Veyra.Access.Keys;

/// <summary>
/// An ingestion key as Veyra stores it: everything except the secret itself.
/// </summary>
/// <param name="Id">The key's ID.</param>
/// <param name="Name">What the key is for, for example <c>web-01 collector</c>.</param>
/// <param name="DisplayPrefix">The start of the key, safe to show, for example <c>vyk_AbCdEfGh</c>.</param>
/// <param name="Environments">The environments the key may send data for. <see langword="null"/> means all.</param>
/// <param name="CreatedBy">The ID of the administrator who created the key.</param>
/// <param name="CreatedAt">When the key was created.</param>
/// <param name="ExpiresAt">When the key stops working by itself, or <see langword="null"/> for never.</param>
/// <param name="RevokedAt">When the key was revoked, or <see langword="null"/> while it is active.</param>
/// <param name="LastUsedAt">When a sender last presented the key, or <see langword="null"/> if never.</param>
internal sealed record IngestionKey(
    Guid Id,
    string Name,
    string DisplayPrefix,
    IReadOnlyList<string>? Environments,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt)
{
    /// <summary>
    /// Whether the key may be used at the given time: not revoked and not expired.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when the key is active.</returns>
    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || now < ExpiresAt);
}
