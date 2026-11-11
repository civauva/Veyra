namespace Veyra.Query.Metrics;

/// <summary>
/// How many values fell into each bucket of a histogram over some period: the increase of a cumulative histogram,
/// or several of them added up.
/// </summary>
/// <param name="Bounds">The upper bounds of every bucket but the last, ascending.</param>
/// <param name="Counts">The values per bucket: one more entry than <paramref name="Bounds"/>.</param>
/// <param name="Sum">The sum of the values, when known.</param>
internal sealed record Distribution(IReadOnlyList<double> Bounds, IReadOnlyList<long> Counts, double? Sum)
{
    /// <summary>All values.</summary>
    public long Count => Counts.Sum();

    /// <summary>Adds another distribution with the same bounds.</summary>
    /// <param name="other">The distribution to add.</param>
    /// <returns>The sum, or this distribution when the bounds differ and the two cannot be added.</returns>
    public Distribution Add(Distribution other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!Bounds.SequenceEqual(other.Bounds))
        {
            return this;
        }

        var counts = Counts.Zip(other.Counts, (a, b) => a + b).ToArray();
        var sum = Sum is { } mine && other.Sum is { } theirs ? mine + theirs : Sum ?? other.Sum;

        return new Distribution(Bounds, counts, sum);
    }

    /// <summary>
    /// Estimates a quantile, the way Prometheus does: find the bucket that holds it and interpolate linearly inside.
    /// The first bucket starts at 0 when its bound is positive; values in the last, open bucket count as its lower
    /// bound, the highest bound there is.
    /// </summary>
    /// <param name="quantile">From 0 to 1, for example 0.95.</param>
    /// <returns>The estimate, or null without values.</returns>
    public double? Quantile(double quantile)
    {
        var total = Count;

        if (total == 0 || Counts.Count == 0)
        {
            return null;
        }

        var rank = quantile * total;
        long below = 0;

        for (var index = 0; index < Counts.Count; index++)
        {
            var count = Counts[index];

            if (below + count >= rank && count > 0)
            {
                if (index >= Bounds.Count)
                {
                    return Bounds.Count == 0 ? null : Bounds[^1];
                }

                var upper = Bounds[index];
                var lower = index == 0 ? Math.Min(0, upper) : Bounds[index - 1];

                return lower + ((upper - lower) * ((rank - below) / count));
            }

            below += count;
        }

        return Bounds.Count == 0 ? null : Bounds[^1];
    }
}
