namespace Veyra.Core.Security;

/// <summary>
/// An ingestion key that Veyra issued for itself.
/// </summary>
/// <param name="KeyId">The key's ID, to revoke it later and to recognise it in <see cref="IngestionKeyCheck"/>.</param>
/// <param name="Secret">The key itself. Only its hash is stored, so the caller must keep it if it needs it again.</param>
public sealed record IssuedIngestionKey(Guid KeyId, string Secret);
