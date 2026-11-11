namespace Veyra.Core.Metrics;

/// <summary>The kinds of OTLP metric Veyra stores (ADR 0010). The numbers are stored in the database.</summary>
public enum MetricKind
{
    /// <summary>A value at a moment, such as memory in use or the load average.</summary>
    Gauge = 1,

    /// <summary>
    /// A running total, such as CPU time or bytes sent. Monotonic sums only grow; others, such as memory used by
    /// state, can also fall and are read like gauges.
    /// </summary>
    Sum = 2,

    /// <summary>A distribution in explicit buckets, such as request durations.</summary>
    Histogram = 3,
}
