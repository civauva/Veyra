namespace Veyra.Query.Server.Api;

/// <summary>A database of an Availability Group on one replica.</summary>
/// <param name="Group">The group.</param>
/// <param name="Replica">The replica.</param>
/// <param name="Database">The database.</param>
/// <param name="QueueBytes">Log still to send or redo.</param>
/// <param name="LagSeconds">How far the secondary is behind.</param>
/// <param name="Synchronizing">Whether it synchronizes.</param>
public sealed record ReplicaDatabaseResponse(
    string Group,
    string Replica,
    string Database,
    double QueueBytes,
    double LagSeconds,
    bool Synchronizing);
