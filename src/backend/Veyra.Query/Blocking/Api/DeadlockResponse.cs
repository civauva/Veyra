namespace Veyra.Query.Blocking.Api;

/// <summary>
/// A deadlock SQL Server broke (ADR 0020, area 5): its sessions and resources, in words and as a graph.
/// </summary>
/// <param name="Key">What identifies it.</param>
/// <param name="At">When it happened.</param>
/// <param name="Summary">One sentence about it, for anyone.</param>
/// <param name="Processes">The sessions caught in it.</param>
/// <param name="Resources">What they locked.</param>
public sealed record DeadlockResponse(
    string Key,
    DateTimeOffset At,
    string Summary,
    IReadOnlyList<DeadlockProcessResponse> Processes,
    IReadOnlyList<DeadlockResourceResponse> Resources);
