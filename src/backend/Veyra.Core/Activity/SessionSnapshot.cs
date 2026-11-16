namespace Veyra.Core.Activity;

/// <summary>One read of a server's live view: its server-wide figures and its busy sessions.</summary>
/// <param name="Block">The server's block.</param>
/// <param name="Environment">The server's environment.</param>
/// <param name="SampledAt">When the server was read.</param>
/// <param name="Server">The server-wide figures.</param>
/// <param name="Sessions">
/// The sessions that ran, blocked, waited, or held a transaction, in no particular order.
/// </param>
public sealed record SessionSnapshot(
    string Block,
    string? Environment,
    DateTimeOffset SampledAt,
    ServerSample Server,
    IReadOnlyList<SessionSample> Sessions);
