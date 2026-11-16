using System.Collections.Concurrent;

namespace Veyra.Agents.Enrollment;

/// <summary>
/// Slows down guessing of one-time codes: after ten wrong codes from one address within a minute, that address is
/// refused for the rest of the minute. A code's 60 random bits make guessing hopeless anyway; this keeps it so cheap
/// for Veyra to refuse that trying costs nothing.
/// </summary>
/// <param name="clock">The clock for the minute.</param>
internal sealed class EnrollThrottle(TimeProvider clock)
{
    private const int _maxFailures = 10;

    private static readonly TimeSpan _window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> _failures =
        new(StringComparer.Ordinal);

    /// <summary>Whether an address is refused for now.</summary>
    /// <param name="client">The caller's address.</param>
    /// <returns><see langword="true"/> after too many wrong codes in the current minute.</returns>
    public bool IsBlocked(string client) =>
        _failures.TryGetValue(client, out var entry)
        && clock.GetUtcNow() - entry.Start < _window
        && entry.Count >= _maxFailures;

    /// <summary>Counts a wrong code from an address.</summary>
    /// <param name="client">The caller's address.</param>
    public void RecordFailure(string client)
    {
        var now = clock.GetUtcNow();

        _failures.AddOrUpdate(
            client,
            _ => (now, 1),
            (_, entry) => now - entry.Start < _window ? (entry.Start, entry.Count + 1) : (now, 1));

        // Old entries are dropped now and then, so the table stays small.
        if (_failures.Count > 10_000)
        {
            foreach (var (key, entry) in _failures)
            {
                if (now - entry.Start >= _window)
                {
                    _failures.TryRemove(key, out _);
                }
            }
        }
    }
}
