namespace Veyra.Access.Sessions;

/// <summary>One signed-in browser, as stored in <c>access.session</c> (ADR 0011).</summary>
/// <param name="Id">The random ID the session cookie carries.</param>
/// <param name="UserId">Whose session it is.</param>
/// <param name="StartedAt">When it was signed in.</param>
/// <param name="LastActiveAt">The last request, to within five minutes.</param>
/// <param name="EndedAt">When it was ended, or <see langword="null"/> while it lasts.</param>
/// <param name="Address">The client address it was signed in from.</param>
/// <param name="UserAgent">The browser's <c>User-Agent</c> header, cut to 512 characters.</param>
/// <param name="Method">How it was signed in.</param>
internal sealed record AccessSession(
    string Id,
    Guid UserId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActiveAt,
    DateTimeOffset? EndedAt,
    string? Address,
    string? UserAgent,
    SessionMethod Method);
