using System.Collections.Concurrent;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// Rate-limits failed ingestion key attempts per client address (ADR 0008).
/// </summary>
/// <remarks>
/// A key with the right shape but the wrong value costs one database lookup, because only valid keys are cached.
/// After <see cref="MaxFailuresPerMinute"/> failures in a minute, a client is refused before any lookup until the
/// minute is over. Correctly configured senders never reach the limit.
/// </remarks>
/// <param name="clock">The clock for the one-minute windows.</param>
internal sealed class FailedKeyThrottle(TimeProvider clock)
{
    /// <summary>Failed attempts allowed per client address and minute.</summary>
    public const int MaxFailuresPerMinute = 20;

    private static readonly TimeSpan _window = TimeSpan.FromMinutes(1);

    // Beyond this many tracked addresses, finished windows are swept, so random addresses cannot fill the memory.
    private const int _sweepThreshold = 10_000;

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    /// <summary>Whether the client must wait before trying a key again.</summary>
    /// <param name="client">The client address.</param>
    /// <returns><see langword="true"/> when the client reached the limit in its current window.</returns>
    public bool IsBlocked(string client) =>
        _windows.TryGetValue(client, out var window)
        && clock.GetUtcNow() < window.Start + _window
        && window.Failures >= MaxFailuresPerMinute;

    /// <summary>Counts one failed attempt.</summary>
    /// <param name="client">The client address.</param>
    public void RecordFailure(string client)
    {
        var now = clock.GetUtcNow();

        _windows.AddOrUpdate(
            client,
            _ => new Window(now, 1),
            (_, current) => now < current.Start + _window
                ? current with { Failures = current.Failures + 1 }
                : new Window(now, 1));

        if (_windows.Count > _sweepThreshold)
        {
            foreach (var (address, window) in _windows)
            {
                if (now >= window.Start + _window)
                {
                    _windows.TryRemove(address, out _);
                }
            }
        }
    }

    private sealed record Window(DateTimeOffset Start, int Failures);
}
