namespace Veyra.Query.ServerOverview.Api;

/// <summary>A SQL Server's instance performance over the window, one value per bucket (ADR 0025).</summary>
/// <param name="BucketSeconds">The width of a bucket.</param>
/// <param name="Starts">The start of every bucket, the oldest first.</param>
/// <param name="Cpu">SQL Server's share of the CPU, 0 to 1.</param>
/// <param name="SqlMemoryRatio">The memory SQL Server uses as a share of the machine's.</param>
/// <param name="WaitMsPerSecond">Milliseconds waited per second, idle waits left out.</param>
public sealed record PerformanceResponse(
    int BucketSeconds,
    IReadOnlyList<DateTimeOffset> Starts,
    IReadOnlyList<double?> Cpu,
    IReadOnlyList<double?> SqlMemoryRatio,
    IReadOnlyList<double?> WaitMsPerSecond);
