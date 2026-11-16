namespace Veyra.Query.Metrics.Api;

/// <summary>A request running now for longer than ten seconds, from the latest query sample.</summary>
/// <param name="Session">The session ID.</param>
/// <param name="Database">The database it runs in.</param>
/// <param name="User">The login that runs it.</param>
/// <param name="ElapsedSeconds">How long it has been running.</param>
/// <param name="Status">Running, runnable, or suspended.</param>
/// <param name="Wait">What it waits on now, such as <c>LCK_M_U</c>; null when it does not wait.</param>
/// <param name="BlockedBy">The session that blocks it; null when nothing does.</param>
/// <param name="Text">The statement.</param>
public sealed record RunningQueryResponse(
    string? Session,
    string? Database,
    string? User,
    double ElapsedSeconds,
    string? Status,
    string? Wait,
    string? BlockedBy,
    string Text);
