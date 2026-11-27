using Veyra.Core.Telemetry;

namespace Veyra.Query.Logs;

/// <summary>Record counts over a time window, for the histogram and the service filter.</summary>
/// <param name="BucketSeconds">The width of each bucket in seconds.</param>
/// <param name="Buckets">One bucket per interval, oldest first, including empty ones.</param>
/// <param name="Services">The services with the most matching records, most first.</param>
/// <param name="Total">The matching records of the whole window, added up on the server (VEY-68).</param>
/// <param name="Errors">Those at Error or worse (VEY-68).</param>
/// <param name="Levels">
/// The matching records of the whole window per level, keyed by severity name, such as <c>Warning</c> (ADR 0024).
/// Levels without records are left out.
/// </param>
public sealed record LogSummaryResponse(
    int BucketSeconds,
    IReadOnlyList<LogBucketResponse> Buckets,
    IReadOnlyList<ServiceCount> Services,
    long Total,
    long Errors,
    IReadOnlyDictionary<string, long> Levels);
