namespace Veyra.Core.Telemetry;

/// <summary>
/// An overview of the records that match a query: a histogram over the time window and the busiest services.
/// </summary>
/// <param name="BucketSize">The width of each bucket.</param>
/// <param name="Buckets">One bucket per interval from the start of the window, oldest first, including empty ones.</param>
/// <param name="Services">The services with the most matching records, most first. Ignores the service filter.</param>
public sealed record LogSummary(TimeSpan BucketSize, IReadOnlyList<LogBucket> Buckets, IReadOnlyList<ServiceCount> Services);
