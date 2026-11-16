namespace Veyra.Query.Server.Api;

/// <summary>A node of the server's failover cluster.</summary>
/// <param name="Node">The node.</param>
/// <param name="State">Its state.</param>
/// <param name="Owner">Whether it runs the instance now.</param>
public sealed record ClusterNodeResponse(string Node, string State, bool Owner);
