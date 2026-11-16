namespace Veyra.Storage.Metrics;

/// <summary>A part of a metric read and the table it is read from.</summary>
/// <param name="From">The start, inclusive.</param>
/// <param name="To">The end, exclusive.</param>
/// <param name="Source">Where its points are read.</param>
internal sealed record MetricReadSegment(DateTimeOffset From, DateTimeOffset To, MetricReadSource Source);
