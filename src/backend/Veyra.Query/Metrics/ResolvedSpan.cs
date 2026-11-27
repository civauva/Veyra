namespace Veyra.Query.Metrics;

/// <summary>A period worked out from a <see cref="MetricSpan"/>.</summary>
/// <param name="From">The start, inclusive.</param>
/// <param name="To">The end.</param>
/// <param name="Name">The named range, or <c>custom</c> for a period given by <c>from</c> or <c>to</c>.</param>
internal sealed record ResolvedSpan(DateTimeOffset From, DateTimeOffset To, string Name)
{
    /// <summary>How long the period is.</summary>
    public TimeSpan Length => To - From;
}
