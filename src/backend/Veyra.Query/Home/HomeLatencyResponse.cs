namespace Veyra.Query.Home;

/// <summary>
/// How fast the environment answers HTTP requests over the last five minutes, from the SDKs'
/// <c>http.server.request.duration</c> metric.
/// </summary>
/// <param name="P95Seconds">The 95th percentile of every service's request durations together.</param>
/// <param name="SlowestService">The service with the highest 95th percentile.</param>
/// <param name="SlowestP95Seconds">Its 95th percentile.</param>
/// <param name="Services">How many services sent request durations.</param>
public sealed record HomeLatencyResponse(
    double P95Seconds,
    string SlowestService,
    double SlowestP95Seconds,
    int Services);
