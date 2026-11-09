using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Veyra.Core.Security;

namespace Veyra.Access.Keys;

/// <summary>
/// Checks ingestion keys for the Ingestion module, with a short in-memory cache so that most OTLP requests do not
/// touch the database.
/// </summary>
/// <remarks>
/// <para>
/// Only valid keys are cached. Caching rejected keys would let anyone who sends random keys fill the memory.
/// </para>
/// <para>
/// A cached key is re-checked against the database after <see cref="AccessOptions.KeyCacheDuration"/>, which bounds
/// how long a revoked key keeps working. Expiry is checked on every call, so an expired key stops at once.
/// </para>
/// </remarks>
/// <param name="store">Where keys are stored.</param>
/// <param name="clock">The clock for expiry and cache times.</param>
/// <param name="options">The Access settings, for the cache duration.</param>
internal sealed class IngestionKeyValidator(
    IngestionKeyStore store,
    TimeProvider clock,
    IOptions<AccessOptions> options) : IIngestionKeyValidator
{
    private readonly ConcurrentDictionary<string, CachedKey> _cache = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async ValueTask<IngestionKeyCheck> ValidateAsync(string presentedKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presentedKey);

        if (!IngestionKeyFormat.LooksLikeKey(presentedKey))
        {
            return IngestionKeyCheck.Invalid;
        }

        var hash = IngestionKeyFormat.Hash(presentedKey);
        var cacheKey = Convert.ToHexString(hash);
        var now = clock.GetUtcNow();

        if (_cache.TryGetValue(cacheKey, out var cached) && now < cached.RecheckAt)
        {
            return cached.Key.IsActiveAt(now) ? ToCheck(cached.Key) : IngestionKeyCheck.Invalid;
        }

        var key = await store.FindByHashAsync(hash, cancellationToken);

        if (key is null || !key.IsActiveAt(now))
        {
            _cache.TryRemove(cacheKey, out _);

            return IngestionKeyCheck.Invalid;
        }

        // Recording use once per cache period keeps the last-used time useful without a write per request.
        await store.TouchAsync(key.Id, now, cancellationToken);

        _cache[cacheKey] = new CachedKey(key, now + options.Value.KeyCacheDuration);

        return ToCheck(key);
    }

    private static IngestionKeyCheck ToCheck(IngestionKey key) => new(true, key.Id, key.Environments);

    private sealed record CachedKey(IngestionKey Key, DateTimeOffset RecheckAt);
}
