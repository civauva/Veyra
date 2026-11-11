namespace Veyra.Query.Metrics.Api;

/// <summary>A host on the Metrics overview, from the Collector's host metrics.</summary>
/// <param name="Name">From <c>host.name</c>.</param>
/// <param name="Environment">The environment its metrics name, if any.</param>
/// <param name="Os">The operating system, from <c>os.description</c> or <c>os.type</c>.</param>
/// <param name="Cpus">How many CPUs report time.</param>
/// <param name="MemoryBytes">Total memory.</param>
/// <param name="LastSeen">When a point last arrived.</param>
/// <param name="CpuRatio">CPU in use over the last five minutes, from 0 to 1.</param>
/// <param name="MemoryRatio">Memory in use, from 0 to 1.</param>
/// <param name="Load">The one-minute load average.</param>
/// <param name="FullestDisk">The disk with the highest share in use.</param>
/// <param name="FillingDisk">The disk that is full soonest at its current pace, within 30 days; null when none.</param>
/// <param name="Cpu">CPU in use over the window, for the sparkline.</param>
public sealed record HostCardResponse(
    string Name,
    string? Environment,
    string? Os,
    int? Cpus,
    double? MemoryBytes,
    DateTimeOffset LastSeen,
    double? CpuRatio,
    double? MemoryRatio,
    double? Load,
    DiskResponse? FullestDisk,
    DiskResponse? FillingDisk,
    ChartLineResponse Cpu);
