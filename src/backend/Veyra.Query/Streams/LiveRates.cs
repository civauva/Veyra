namespace Veyra.Query.Streams;

/// <summary>
/// Counts what was written per second and environment over a moving window of ten seconds, for the rate in the top
/// bar (ADR 0022).
/// </summary>
/// <remarks>
/// Each environment keeps one slot per second in a ring, so adding and reading cost the same however busy Veyra is.
/// The second in progress is left out of a read, so the rate does not dip at the start of every second.
/// </remarks>
/// <param name="clock">The clock that says which second it is.</param>
internal sealed class LiveRates(TimeProvider clock)
{
    /// <summary>How many whole seconds a rate covers.</summary>
    public const int WindowSeconds = 10;

    // Beyond this many environments, new names are counted in every rate but in none of their own, so a sender that
    // invents environment names cannot grow memory without bound.
    private const int _maxEnvironments = 1_000;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Slot[]> _named = new(StringComparer.Ordinal);
    private readonly Slot[] _unnamed = NewRing();
    private readonly Slot[] _overflow = NewRing();

    /// <summary>Counts what one environment wrote.</summary>
    /// <param name="signal">Logs, spans, or metric points.</param>
    /// <param name="environment">The environment, or <see langword="null"/> for none.</param>
    /// <param name="count">How many.</param>
    public void Add(LiveSignal signal, string? environment, long count)
    {
        var second = clock.GetUtcNow().ToUnixTimeSeconds();

        lock (_lock)
        {
            ref var slot = ref RingOf(environment)[second % (WindowSeconds + 1)];

            if (slot.Second != second)
            {
                slot = new Slot { Second = second };
            }

            switch (signal)
            {
                case LiveSignal.Logs:
                    slot.Logs += count;
                    break;
                case LiveSignal.Spans:
                    slot.Spans += count;
                    break;
                default:
                    slot.Points += count;
                    break;
            }
        }
    }

    /// <summary>Reads the rates of the last ten whole seconds.</summary>
    /// <param name="environments">
    /// Only these environments, or <see langword="null"/> for everything, with or without an environment.
    /// </param>
    /// <returns>Events per second.</returns>
    public LiveRate Read(IReadOnlyCollection<string>? environments)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        long logs = 0;
        long spans = 0;
        long points = 0;

        lock (_lock)
        {
            foreach (var ring in RingsOf(environments))
            {
                foreach (var slot in ring)
                {
                    if (slot.Second >= now - WindowSeconds && slot.Second < now)
                    {
                        logs += slot.Logs;
                        spans += slot.Spans;
                        points += slot.Points;
                    }
                }
            }
        }

        return new LiveRate(
            (double)logs / WindowSeconds,
            (double)spans / WindowSeconds,
            (double)points / WindowSeconds);
    }

    private Slot[] RingOf(string? environment)
    {
        if (environment is null)
        {
            return _unnamed;
        }

        if (_named.TryGetValue(environment, out var ring))
        {
            return ring;
        }

        if (_named.Count >= _maxEnvironments)
        {
            return _overflow;
        }

        ring = NewRing();
        _named[environment] = ring;

        return ring;
    }

    private IEnumerable<Slot[]> RingsOf(IReadOnlyCollection<string>? environments)
    {
        if (environments is null)
        {
            return _named.Values.Append(_unnamed).Append(_overflow);
        }

        return environments
            .Distinct(StringComparer.Ordinal)
            .Select(environment => _named.GetValueOrDefault(environment))
            .OfType<Slot[]>();
    }

    // A slot no second has used yet holds second -1, which no read takes.
    private static Slot[] NewRing() => Enumerable.Repeat(new Slot { Second = -1 }, WindowSeconds + 1).ToArray();

    private struct Slot
    {
        public long Second;
        public long Logs;
        public long Spans;
        public long Points;
    }
}
