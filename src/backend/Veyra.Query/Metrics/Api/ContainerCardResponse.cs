namespace Veyra.Query.Metrics.Api;

/// <summary>
/// A Docker container on the Metrics overview and on its host's screen, from the Collector's <c>docker_stats</c>
/// receiver (ADR 0018).
/// </summary>
/// <param name="Name">From <c>container.name</c>.</param>
/// <param name="Host">The host it runs on, from <c>host.name</c>; null when the Collector did not add it.</param>
/// <param name="Environment">The environment its metrics name, if any.</param>
/// <param name="Image">The image, from <c>container.image.name</c>.</param>
/// <param name="Id">The short container ID: the first 12 characters, as Docker shows it.</param>
/// <param name="LastSeen">When a point last arrived.</param>
/// <param name="CpuPercent">
/// CPU in use over the last five minutes, in percent of one CPU: above 100 when it uses more than one.
/// </param>
/// <param name="MemoryBytes">Memory in use now, without the page cache.</param>
/// <param name="MemoryLimitBytes">
/// Its memory limit. Docker reports the host's memory for a container without a limit.
/// </param>
/// <param name="MemoryRatio">Memory in use of its limit, from 0 to 1.</param>
/// <param name="Restarts">Restarts within the window; null when the container sends no restart count.</param>
/// <param name="RestartsTotal">Docker's count of its restarts since it was created.</param>
/// <param name="UptimeSeconds">How long it has run since it last started.</param>
/// <param name="Cpu">CPU in use over the window, in percent of one CPU, for the sparkline.</param>
public sealed record ContainerCardResponse(
    string Name,
    string? Host,
    string? Environment,
    string? Image,
    string? Id,
    DateTimeOffset LastSeen,
    double? CpuPercent,
    double? MemoryBytes,
    double? MemoryLimitBytes,
    double? MemoryRatio,
    double? Restarts,
    double? RestartsTotal,
    double? UptimeSeconds,
    ChartLineResponse Cpu);
