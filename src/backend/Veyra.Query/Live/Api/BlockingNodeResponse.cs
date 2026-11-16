namespace Veyra.Query.Live.Api;

/// <summary>A session in a blocking chain, with the sessions that wait for it (ADR 0020, area 3).</summary>
/// <param name="Session">The session's ID; its details are among the live view's sessions.</param>
/// <param name="Waiting">How many sessions wait for it, directly or through others.</param>
/// <param name="Children">The sessions that wait for it directly, the longest waiting first.</param>
public sealed record BlockingNodeResponse(int Session, int Waiting, IReadOnlyList<BlockingNodeResponse> Children);
