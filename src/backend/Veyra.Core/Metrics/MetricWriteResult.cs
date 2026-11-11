namespace Veyra.Core.Metrics;

/// <summary>What happened to a batch of metric points.</summary>
/// <param name="Stored">Points written.</param>
/// <param name="Dropped">Points of new series dropped because the series limit was reached.</param>
public sealed record MetricWriteResult(int Stored, int Dropped);
