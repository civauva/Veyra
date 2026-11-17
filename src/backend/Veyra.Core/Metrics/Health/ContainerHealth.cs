namespace Veyra.Core.Metrics.Health;

/// <summary>A Docker container as the built-in container rules judge it now (ADR 0018).</summary>
/// <param name="Container">The container's block name, from <c>container.name</c>.</param>
/// <param name="Host">
/// The host it runs on, from <c>host.name</c>; <see langword="null"/> when the Collector did not add it.
/// </param>
/// <param name="Environment">Its environment, or <see langword="null"/> when its metrics name none.</param>
/// <param name="Restarts">
/// Restarts in the last 15 minutes; <see langword="null"/> when the container sends no restart count.
/// </param>
/// <param name="MemoryRatio">
/// The share of its memory limit in use now, from 0 to 1; <see langword="null"/> when it sends no memory figures.
/// Docker reports the host's memory as the limit of a container without one.
/// </param>
/// <param name="MemoryUsedBytes">The memory in use now, without the page cache.</param>
/// <param name="MemoryLimitBytes">The memory limit.</param>
public sealed record ContainerHealth(
    string Container,
    string? Host,
    string? Environment,
    double? Restarts,
    double? MemoryRatio,
    double? MemoryUsedBytes,
    double? MemoryLimitBytes);
