namespace Veyra.Query.Metrics.Api;

/// <summary>One host's metrics over a window.</summary>
/// <param name="Card">The numbers of its overview card.</param>
/// <param name="Range">The window, for example <c>6h</c>.</param>
/// <param name="BucketSeconds">The width of each point.</param>
/// <param name="Cpu">CPU in use: <c>total</c>, <c>user</c>, <c>system</c>, and <c>wait</c>, from 0 to 1.</param>
/// <param name="Memory">Memory: <c>used</c> and <c>cache</c>, from 0 to 1 of the total.</param>
/// <param name="Disks">Every mounted filesystem, fullest first.</param>
/// <param name="Network">Bytes per second: <c>received</c> and <c>sent</c>.</param>
/// <param name="DiskIo">Bytes per second: <c>read</c> and <c>written</c>.</param>
/// <param name="Load">The load averages: <c>1m</c>, <c>5m</c>, and <c>15m</c>.</param>
public sealed record HostMetricsResponse(
    HostCardResponse Card,
    string Range,
    int BucketSeconds,
    IReadOnlyList<ChartLineResponse> Cpu,
    IReadOnlyList<ChartLineResponse> Memory,
    IReadOnlyList<DiskResponse> Disks,
    IReadOnlyList<ChartLineResponse> Network,
    IReadOnlyList<ChartLineResponse> DiskIo,
    IReadOnlyList<ChartLineResponse> Load);
