namespace Veyra.Core.Telemetry;

/// <summary>
/// An overview of the records that match a query: a histogram over the time window and the busiest services.
/// </summary>
/// <param name="BucketSize">The width of each bucket.</param>
/// <param name="Buckets">One bucket per interval from the start of the window, oldest first, including empty ones.</param>
/// <param name="Services">The services with the most matching records, most first. Ignores the service filter.</param>
/// <param name="LevelsPerBucket">
/// The records of each bucket per severity, in the order of <paramref name="Buckets"/>, for a chart stacked by level
/// (ADR 0024). A severity without records is left out. <see langword="null"/> where a source does not count them.
/// </param>
public sealed record LogSummary(
    TimeSpan BucketSize,
    IReadOnlyList<LogBucket> Buckets,
    IReadOnlyList<ServiceCount> Services,
    IReadOnlyList<IReadOnlyDictionary<LogSeverity, long>>? LevelsPerBucket = null);
