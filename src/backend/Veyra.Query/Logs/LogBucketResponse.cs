namespace Veyra.Query.Logs;

/// <summary>One bar of the log histogram, as <c>GET /api/v1/logs/summary</c> answers it.</summary>
/// <param name="Start">The start of the interval.</param>
/// <param name="Total">All matching records in the interval.</param>
/// <param name="Errors">Those at Error or worse.</param>
/// <param name="Levels">
/// The records of the interval per level, keyed by severity name, so the chart can stack them (ADR 0024). Levels
/// without records are left out.
/// </param>
public sealed record LogBucketResponse(
    DateTimeOffset Start,
    long Total,
    long Errors,
    IReadOnlyDictionary<string, long> Levels);
