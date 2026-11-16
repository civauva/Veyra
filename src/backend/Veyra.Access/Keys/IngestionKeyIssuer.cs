using Veyra.Core.Security;

namespace Veyra.Access.Keys;

/// <summary>
/// Issues the keys other modules need, as ordinary ingestion keys without a creator.
/// </summary>
/// <param name="keys">Creates and revokes the keys.</param>
internal sealed class IngestionKeyIssuer(IngestionKeyService keys) : IIngestionKeyIssuer
{
    /// <inheritdoc />
    public async Task<IssuedIngestionKey> IssueAsync(string name, CancellationToken cancellationToken)
    {
        var created = await keys.CreateAsync(name, null, null, null, cancellationToken);

        return new IssuedIngestionKey(created.Key.Id, created.Secret);
    }

    /// <inheritdoc />
    public Task RevokeAsync(Guid keyId, CancellationToken cancellationToken) =>
        keys.RevokeAsync(keyId, null, cancellationToken);
}
