namespace Veyra.Storage.Metrics;

/// <summary>Where a part of a metric read comes from (ADR 0015).</summary>
internal enum MetricReadSource
{
    /// <summary>The points themselves: short buckets, the newest minutes, and times not rolled up yet.</summary>
    Points,

    /// <summary>The 5-minute rollups in <c>storage.metric_rollup_5m</c>.</summary>
    FiveMinutes,

    /// <summary>The hourly rollups in <c>storage.metric_rollup_1h</c>.</summary>
    Hours,
}
