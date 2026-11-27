namespace Veyra.Query.Server.Api;

/// <summary>A server's availability (ADR 0020, area 12).</summary>
/// <param name="Replicas">Its Availability Group replicas.</param>
/// <param name="Databases">Its Availability Group databases with their queues.</param>
/// <param name="LogShipping">Its log-shipped databases.</param>
/// <param name="Nodes">The nodes of its failover cluster.</param>
public sealed record AvailabilityResponse(
    IReadOnlyList<ReplicaResponse> Replicas,
    IReadOnlyList<ReplicaDatabaseResponse> Databases,
    IReadOnlyList<LogShippingResponse> LogShipping,
    IReadOnlyList<ClusterNodeResponse> Nodes)
{
    /// <summary>
    /// What is not well, in words: each replica that is not healthy, each database that is not synchronizing, and
    /// each log shipping that is behind, so the screen tells it without looking through the lists (VEY-68).
    /// </summary>
    public IReadOnlyList<string> Trouble =>
    [
        .. Replicas.Where(one => !one.Healthy).Select(one => $"replica {one.Replica} of {one.Group} is not healthy"),
        .. Databases
            .Where(one => !one.Synchronizing)
            .Select(one => $"{one.Database} on {one.Replica} is not synchronizing"),
        .. LogShipping.Where(one => one.Behind).Select(one => $"log shipping of {one.Database} is behind"),
    ];
}
