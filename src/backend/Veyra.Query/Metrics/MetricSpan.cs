namespace Veyra.Query.Metrics;

/// <summary>
/// The period a request asks for (ADR 0022): a named range ending now, such as <c>6h</c>, or a period between
/// <c>from</c> and <c>to</c>. A screen sends the same <c>from</c> and <c>to</c> to every endpoint, so every figure
/// covers the same window.
/// </summary>
/// <param name="Range">
/// A named range: <c>15m</c>, <c>1h</c>, <c>6h</c>, <c>24h</c>, <c>7d</c>, or <c>30d</c>; anything else means
/// <c>6h</c>. With only <paramref name="To"/>, it is the length before it.
/// </param>
/// <param name="From">The start of the period, inclusive; null for a named range.</param>
/// <param name="To">The end of the period; null for now.</param>
internal sealed record MetricSpan(string? Range, DateTimeOffset? From = null, DateTimeOffset? To = null)
{
    /// <summary>The longest period one request may cover, as logs allow (ADR 0022).</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromDays(31);

    /// <summary>The range used when none, or an unknown one, is asked for.</summary>
    public const string DefaultRange = "6h";

    /// <summary>The name of a period given by <c>from</c> or <c>to</c>.</summary>
    public const string CustomRange = "custom";

    /// <summary>The problem a refused period answers with.</summary>
    public static readonly string Refusal =
        $"The time window must be positive and at most {MaxLength.TotalDays} days.";

    /// <summary>A named range ending now.</summary>
    /// <param name="range">The name, such as <c>6h</c>.</param>
    /// <returns>The span.</returns>
    public static MetricSpan FromString(string? range) => new(range);

    /// <summary>Reads a named range as a span, so callers that only know a range keep passing it.</summary>
    /// <param name="range">The name, such as <c>6h</c>.</param>
    public static implicit operator MetricSpan(string? range) => FromString(range);

    /// <summary>Works out the period.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>The period, or null when it is not positive or longer than <see cref="MaxLength"/>.</returns>
    public ResolvedSpan? Resolve(DateTimeOffset now)
    {
        var name = Range is not null && MetricWindow.Ranges.ContainsKey(Range) ? Range : DefaultRange;
        var length = MetricWindow.Ranges[name];

        if (From is null && To is null)
        {
            return new ResolvedSpan(now - length, now, name);
        }

        var to = To ?? now;
        var from = From ?? to - length;

        if (from >= to || to - from > MaxLength)
        {
            return null;
        }

        return new ResolvedSpan(from, to, CustomRange);
    }
}
