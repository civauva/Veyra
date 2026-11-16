namespace Veyra.Core.Security;

/// <summary>
/// Issues and revokes ingestion keys that Veyra itself needs, such as the key of a Veyra agent (ADR 0019). The Access
/// module implements it; other modules call it without referencing the Access module.
/// </summary>
/// <remarks>
/// An issued key is an ordinary ingestion key: it appears on the Ingestion keys page, without a creator, and is
/// validated like every other key.
/// </remarks>
public interface IIngestionKeyIssuer
{
    /// <summary>
    /// Creates a key for every environment that never expires, and returns its secret, which is never available again.
    /// </summary>
    /// <param name="name">What the key is for, as the Ingestion keys page shows it.</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    /// <returns>The new key's ID and its secret.</returns>
    Task<IssuedIngestionKey> IssueAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a key. Senders using it are rejected once the validator's short cache has expired.
    /// </summary>
    /// <param name="keyId">The key's ID.</param>
    /// <param name="cancellationToken">Cancels the revocation.</param>
    /// <returns>A task that completes when the key is revoked. Revoking an unknown or revoked key does nothing.</returns>
    Task RevokeAsync(Guid keyId, CancellationToken cancellationToken);
}
