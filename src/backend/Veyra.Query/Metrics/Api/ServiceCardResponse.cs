namespace Veyra.Query.Metrics.Api;

/// <summary>A service on the Metrics overview, from the OpenTelemetry SDK's HTTP server and runtime metrics.</summary>
/// <param name="Name">From <c>service.name</c>.</param>
/// <param name="Environment">From <c>deployment.environment.name</c>, if set.</param>
/// <param name="Runtime">The SDK's language, for example <c>.NET</c>.</param>
/// <param name="Instances">How many instances send metrics.</param>
/// <param name="LastSeen">When a point last arrived.</param>
/// <param name="RequestsPerSecond">HTTP requests served per second over the last five minutes.</param>
/// <param name="P95Seconds">The 95th percentile of their duration.</param>
/// <param name="ServerErrorRatio">The share answered with a 5xx status, from 0 to 1.</param>
/// <param name="CpuRatio">CPU in use by the processes, from 0 to 1 of the CPUs they may use.</param>
/// <param name="MemoryBytes">Memory the processes use.</param>
/// <param name="Latency">The 95th percentile over the window, for the sparkline.</param>
public sealed record ServiceCardResponse(
    string Name,
    string? Environment,
    string? Runtime,
    int Instances,
    DateTimeOffset LastSeen,
    double? RequestsPerSecond,
    double? P95Seconds,
    double? ServerErrorRatio,
    double? CpuRatio,
    double? MemoryBytes,
    ChartLineResponse Latency);
