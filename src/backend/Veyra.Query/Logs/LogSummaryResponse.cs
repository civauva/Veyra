using Veyra.Core.Telemetry;

namespace Veyra.Query.Logs;

/// <summary>Record counts over a time window, for the histogram and the service filter.</summary>
/// <param name="BucketSeconds">The width of each bucket in seconds.</param>
/// <param name="Buckets">One bucket per interval, oldest first, including empty ones.</param>
/// <param name="Services">The services with the most matching records, most first.</param>
public sealed record LogSummaryResponse(
    int BucketSeconds,
    IReadOnlyList<LogBucket> Buckets,
    IReadOnlyList<ServiceCount> Services);
