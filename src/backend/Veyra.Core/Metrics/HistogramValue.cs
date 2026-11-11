namespace Veyra.Core.Metrics;

/// <summary>An explicit-bucket histogram point.</summary>
/// <param name="Count">How many values were recorded.</param>
/// <param name="Sum">Their sum, when sent.</param>
/// <param name="Min">The smallest value, when sent.</param>
/// <param name="Max">The largest value, when sent.</param>
/// <param name="Bounds">The upper bounds of every bucket but the last, ascending.</param>
/// <param name="Counts">How many values fell into each bucket: one more entry than <paramref name="Bounds"/>.</param>
public sealed record HistogramValue(
    long Count,
    double? Sum,
    double? Min,
    double? Max,
    IReadOnlyList<double> Bounds,
    IReadOnlyList<long> Counts);
