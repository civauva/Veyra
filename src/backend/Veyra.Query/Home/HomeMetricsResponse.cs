namespace Veyra.Query.Home;

/// <summary>
/// The key metrics the home shows as tiles, measured over the last few minutes.
/// </summary>
/// <param name="ErrorRatio">The share of records that are errors, or null without records.</param>
/// <param name="IngestPerSecond">Records received per second.</param>
/// <param name="Records">All records in the window.</param>
/// <param name="WindowSeconds">The window length in seconds.</param>
/// <param name="Latency">How fast HTTP requests are answered, from metrics; null when no service sends them.</param>
public sealed record HomeMetricsResponse(
    double? ErrorRatio,
    double IngestPerSecond,
    long Records,
    int WindowSeconds,
    HomeLatencyResponse? Latency = null);
