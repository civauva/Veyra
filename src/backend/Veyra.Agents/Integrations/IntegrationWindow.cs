namespace Veyra.Agents.Integrations;

/// <summary>
/// The period the integration page's figures cover (ADR 0022): a named range ending now, or <c>from</c> and
/// <c>to</c>, refused past 31 days, as every other screen reads it. Its line has 60 buckets of whole minutes.
/// </summary>
/// <param name="From">The start, inclusive.</param>
/// <param name="To">The end, exclusive.</param>
/// <param name="Bucket">The width of each point of a module's line.</param>
internal sealed record IntegrationWindow(DateTimeOffset From, DateTimeOffset To, TimeSpan Bucket)
{
    /// <summary>The longest period one request may cover.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromDays(31);

    /// <summary>The problem a refused period answers with.</summary>
    public const string Refusal = "The time window must be positive and at most 31 days.";

    // The points of a module's line.
    private const int _points = 60;

    private static readonly Dictionary<string, TimeSpan> _ranges = new(StringComparer.Ordinal)
    {
        ["15m"] = TimeSpan.FromMinutes(15),
        ["1h"] = TimeSpan.FromHours(1),
        ["6h"] = TimeSpan.FromHours(6),
        ["24h"] = TimeSpan.FromHours(24),
        ["7d"] = TimeSpan.FromDays(7),
        ["30d"] = TimeSpan.FromDays(30),
    };

    /// <summary>The number of points of a module's line.</summary>
    public int Points => (int)Math.Ceiling((To - From) / Bucket);

    /// <summary>Works out the period.</summary>
    /// <param name="range">A named range, such as <c>6h</c>; anything else means <c>6h</c>.</param>
    /// <param name="from">The start, instead of a range.</param>
    /// <param name="to">The end; now when left out.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The window, or <see langword="null"/> when it is not positive or longer than 31 days.</returns>
    public static IntegrationWindow? Resolve(
        string? range,
        DateTimeOffset? from,
        DateTimeOffset? to,
        DateTimeOffset now)
    {
        var length = range is not null && _ranges.TryGetValue(range, out var named) ? named : _ranges["6h"];
        var end = to ?? now;
        var start = from ?? end - length;

        if (start >= end || end - start > MaxLength)
        {
            return null;
        }

        var minutes = Math.Max(1, (long)Math.Ceiling((end - start).TotalMinutes / _points));

        return new IntegrationWindow(start, end, TimeSpan.FromMinutes(minutes));
    }
}
