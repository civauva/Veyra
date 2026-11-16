namespace Veyra.Query.Blocking.Api;

/// <summary>
/// A blocking episode (ADR 0020, area 5): one session holding others up, from the first read that saw it until the
/// last.
/// </summary>
/// <param name="Start">The first read that saw it.</param>
/// <param name="End">The last read that saw it.</param>
/// <param name="Head">The head blocker's session ID.</param>
/// <param name="Program">The head blocker's program.</param>
/// <param name="Host">The head blocker's machine.</param>
/// <param name="Sleeping">Whether the head blocker slept with an open transaction.</param>
/// <param name="HeadName">What the head blocker's statement does, in words.</param>
/// <param name="HeadText">The head blocker's statement or last batch.</param>
/// <param name="MostWaiting">The most sessions waiting behind it in one read.</param>
/// <param name="LongestWaitSeconds">The longest any of them waited.</param>
/// <param name="WaitingPrograms">The programs that waited.</param>
/// <param name="Summary">One sentence about it, for anyone.</param>
public sealed record EpisodeResponse(
    DateTimeOffset Start,
    DateTimeOffset End,
    int Head,
    string Program,
    string Host,
    bool Sleeping,
    string HeadName,
    string HeadText,
    int MostWaiting,
    double LongestWaitSeconds,
    IReadOnlyList<string> WaitingPrograms,
    string Summary);
