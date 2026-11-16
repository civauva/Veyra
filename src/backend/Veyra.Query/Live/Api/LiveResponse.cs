namespace Veyra.Query.Live.Api;

/// <summary>
/// A SQL Server's Live tab (ADR 0020, area 3): its newest read, or the read at a chosen moment, with what Veyra makes
/// of it.
/// </summary>
/// <param name="Engine">The engine, such as <c>microsoft.sql_server</c>.</param>
/// <param name="Server">The server's name.</param>
/// <param name="Environment">The server's environment.</param>
/// <param name="SampledAt">When the read was made, or <see langword="null"/> when there is none.</param>
/// <param name="AgeSeconds">How old the read is.</param>
/// <param name="At">The moment asked for, or <see langword="null"/> for now.</param>
/// <param name="Tiles">The numbers at the top.</param>
/// <param name="Findings">What Veyra found, the worst first; one <c>ok</c> finding when nothing is wrong.</param>
/// <param name="Blocking">The blocking chains, head blockers first, the one with the most waiting first.</param>
/// <param name="Sessions">The busy sessions, the longest running first.</param>
/// <param name="Waits">Every wait type the sessions wait on, explained.</param>
public sealed record LiveResponse(
    string Engine,
    string Server,
    string? Environment,
    DateTimeOffset? SampledAt,
    double? AgeSeconds,
    DateTimeOffset? At,
    LiveTilesResponse Tiles,
    IReadOnlyList<FindingResponse> Findings,
    IReadOnlyList<BlockingNodeResponse> Blocking,
    IReadOnlyList<LiveSessionResponse> Sessions,
    IReadOnlyDictionary<string, WaitTermResponse> Waits);
