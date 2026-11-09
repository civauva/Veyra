namespace Veyra.Core.Security;

/// <summary>
/// The result of checking an ingestion key.
/// </summary>
/// <param name="IsValid">Whether the key exists, is not revoked, and has not expired.</param>
/// <param name="KeyId">The key's ID when it is valid, for logging and for its last-used time.</param>
/// <param name="AllowedEnvironments">
/// The environments the key may send data for, matched against <c>deployment.environment.name</c>.
/// <see langword="null"/> means every environment.
/// </param>
public sealed record IngestionKeyCheck(bool IsValid, Guid? KeyId, IReadOnlyList<string>? AllowedEnvironments)
{
    /// <summary>The result for a missing, unknown, revoked, or expired key.</summary>
    public static IngestionKeyCheck Invalid { get; } = new(false, null, null);
}
