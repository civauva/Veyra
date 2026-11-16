using Veyra.Core.Metrics;

namespace Veyra.Query.Metrics;

/// <summary>A database server's statement series and their totals over a window (VEY-43).</summary>
/// <param name="Series">The statement series: four per statement, told apart by their attributes.</param>
/// <param name="Buckets">What each series added over the window, usually one bucket per series.</param>
internal sealed record StatementTotals(IReadOnlyList<MetricSeries> Series, IReadOnlyList<MetricBucket> Buckets);
