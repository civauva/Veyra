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
    IReadOnlyList<ClusterNodeResponse> Nodes);
