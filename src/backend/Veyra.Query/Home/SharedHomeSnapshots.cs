namespace Veyra.Query.Home;

/// <summary>
/// Builds the home snapshot once per environment every <see cref="Lifetime"/> and gives it to everyone who asks
/// (ADR 0015), so twenty open homes cost what one does. Requests that arrive while a snapshot is being built wait for
/// that build instead of starting their own.
/// </summary>
/// <remarks>
/// The snapshot is the same for every viewer: it holds no per-person data, and times are sent in UTC for the browser to
/// show in each person's zone. A failed build is not kept, so the next request tries again.
/// </remarks>
/// <param name="build">Builds the snapshot of one environment, or of all of them for null.</param>
/// <param name="clock">The clock that decides when a snapshot is too old.</param>
internal sealed class SharedHomeSnapshots(
    Func<string?, CancellationToken, Task<HomeResponse>> build,
    TimeProvider clock)
{
    /// <summary>
    /// How long a snapshot is shared. The frontend asks every 15 seconds, so one browser never sees the same one twice.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();

    private readonly Dictionary<string, (DateTimeOffset StartedAt, Task<HomeResponse> Build)> _entries =
        new(StringComparer.Ordinal);

    /// <summary>Returns a snapshot of the environment no older than <see cref="Lifetime"/>.</summary>
    /// <param name="environment">The environment, or null for all of them.</param>
    /// <param name="cancellationToken">Stops this caller waiting; the shared build goes on for the others.</param>
    /// <returns>The snapshot.</returns>
    public Task<HomeResponse> GetAsync(string? environment, CancellationToken cancellationToken)
    {
        var key = environment ?? string.Empty;
        var now = clock.GetUtcNow();
        Task<HomeResponse> shared;

        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var entry) && now - entry.StartedAt < Lifetime && !entry.Build.IsFaulted)
            {
                shared = entry.Build;
            }
            else
            {
                // Snapshots nobody asked for in a while go, so environments named once are not kept for ever.
                foreach (var old in _entries.Where(pair => now - pair.Value.StartedAt >= Lifetime).ToList())
                {
                    _entries.Remove(old.Key);
                }

                // The build belongs to everyone who waits for it, so no single caller can cancel it.
                shared = build(environment, CancellationToken.None);
                _entries[key] = (now, shared);
            }
        }

        return shared.WaitAsync(cancellationToken);
    }
}
