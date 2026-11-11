using Veyra.Core.Metrics;

namespace Veyra.Ingestion.Otlp;

/// <summary>The points of one OTLP metrics request, and those Veyra cannot store.</summary>
/// <param name="Points">The points to store.</param>
/// <param name="Refused">Points of kinds Veyra does not store, or that could not be read.</param>
/// <param name="RefusalReason">Why points were refused, for OTLP partial success; null when none were.</param>
internal sealed record MappedMetrics(IReadOnlyList<MetricPoint> Points, int Refused, string? RefusalReason);
