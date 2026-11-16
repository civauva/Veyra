namespace Veyra.Query.Server.Api;

/// <summary>An Availability Group replica.</summary>
/// <param name="Group">The group.</param>
/// <param name="Replica">The replica's server.</param>
/// <param name="Role">Its role, such as PRIMARY.</param>
/// <param name="Healthy">Whether its synchronization is healthy.</param>
public sealed record ReplicaResponse(string Group, string Replica, string Role, bool Healthy);
