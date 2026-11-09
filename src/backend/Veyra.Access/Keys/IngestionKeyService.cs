using Microsoft.Extensions.Logging;

namespace Veyra.Access.Keys;

/// <summary>
/// Creates, lists, and revokes ingestion keys. Only administrators reach it, through the ingestion keys endpoints.
/// </summary>
/// <param name="store">Where keys are stored.</param>
/// <param name="clock">The clock for creation and revocation times.</param>
/// <param name="logger">Receives the security events of ADR 0008.</param>
internal sealed partial class IngestionKeyService(
    IngestionKeyStore store,
    TimeProvider clock,
    ILogger<IngestionKeyService> logger)
{
    /// <summary>
    /// Creates a key and returns its secret, which is never available again.
    /// </summary>
    /// <param name="name">What the key is for.</param>
    /// <param name="environments">
    /// The environments the key may send data for, or <see langword="null"/> for all.
    /// </param>
    /// <param name="expiresAt">When the key stops working by itself, or <see langword="null"/> for never.</param>
    /// <param name="createdBy">The administrator who creates the key.</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    /// <returns>The stored key and its secret.</returns>
    public async Task<CreatedIngestionKey> CreateAsync(
        string name,
        IReadOnlyList<string>? environments,
        DateTimeOffset? expiresAt,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        var secret = IngestionKeyFormat.Generate();
        var key = new IngestionKey(
            Id: Guid.CreateVersion7(),
            Name: name.Trim(),
            DisplayPrefix: IngestionKeyFormat.DisplayPrefix(secret),
            Environments: environments is { Count: > 0 } ? environments : null,
            CreatedBy: createdBy,
            CreatedAt: clock.GetUtcNow(),
            ExpiresAt: expiresAt,
            RevokedAt: null,
            LastUsedAt: null);

        await store.InsertAsync(key, IngestionKeyFormat.Hash(secret), cancellationToken);

        LogKeyCreated(key.Id, key.Name, key.DisplayPrefix, createdBy);

        return new CreatedIngestionKey(key, secret);
    }

    /// <summary>Lists all keys, newest first, without their secrets.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The keys.</returns>
    public Task<IReadOnlyList<IngestionKey>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    /// <summary>
    /// Revokes a key. Senders using it are rejected once the validator's cache has expired.
    /// </summary>
    /// <param name="id">The key's ID.</param>
    /// <param name="revokedBy">The administrator who revokes the key.</param>
    /// <param name="cancellationToken">Cancels the revocation.</param>
    /// <returns><see langword="true"/> when an active key was revoked; <see langword="false"/> otherwise.</returns>
    public async Task<bool> RevokeAsync(Guid id, Guid revokedBy, CancellationToken cancellationToken)
    {
        var revoked = await store.RevokeAsync(id, clock.GetUtcNow(), cancellationToken);

        if (revoked)
        {
            LogKeyRevoked(id, revokedBy);
        }

        return revoked;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Security: ingestion key {KeyId} {KeyName} ({DisplayPrefix}) created by user {UserId}")]
    private partial void LogKeyCreated(Guid keyId, string keyName, string displayPrefix, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: ingestion key {KeyId} revoked by user {UserId}")]
    private partial void LogKeyRevoked(Guid keyId, Guid userId);
}
