namespace Veyra.Access.Keys;

/// <summary>
/// A newly created ingestion key together with its secret.
/// </summary>
/// <remarks>
/// This is the only moment the secret exists outside the sender's configuration. It must be returned to the
/// administrator once and then forgotten.
/// </remarks>
/// <param name="Key">The stored key.</param>
/// <param name="Secret">The full key to configure in the sender.</param>
internal sealed record CreatedIngestionKey(IngestionKey Key, string Secret);
