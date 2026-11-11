using System.Globalization;
using Microsoft.Extensions.Options;
using Veyra.Access.History;
using Veyra.Access.Users;

namespace Veyra.Access.Sessions;

/// <summary>
/// Starts, checks, lists, and ends sessions (ADR 0011). Every request with a session cookie is checked here, so an
/// ended session or a switched-off user is refused at once.
/// </summary>
/// <param name="store">Where sessions are stored.</param>
/// <param name="events">Where ending sessions by hand is recorded.</param>
/// <param name="options">The Access settings, for the idle timeout.</param>
/// <param name="clock">The clock for start, activity, and end times.</param>
internal sealed class SessionService(
    SessionStore store,
    UserEventStore events,
    IOptions<AccessOptions> options,
    TimeProvider clock)
{
    /// <summary>Starts a session after a successful sign-in.</summary>
    /// <param name="userId">Who signed in.</param>
    /// <param name="method">How they signed in.</param>
    /// <param name="client">Where they signed in from.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The new session.</returns>
    public async Task<AccessSession> StartAsync(
        Guid userId,
        SessionMethod method,
        SignInClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var now = clock.GetUtcNow();
        var session = new AccessSession(
            SessionStore.NewId(),
            userId,
            now,
            now,
            null,
            client.Address,
            client.UserAgent,
            method);

        await store.InsertAsync(session, cancellationToken);

        return session;
    }

    /// <summary>
    /// Checks the session of a request and records its activity.
    /// </summary>
    /// <param name="sessionId">The session ID from the cookie.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The user, or <see langword="null"/> when the session is unknown, ended, idle too long, or its user
    /// is switched off.</returns>
    public async Task<AccessUser?> ValidateAsync(string sessionId, CancellationToken cancellationToken)
    {
        var found = await store.FindWithUserAsync(sessionId, cancellationToken);

        if (found is not var (session, user) || session.EndedAt is not null || user.Disabled)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        if (session.LastActiveAt < now - options.Value.SessionIdleTimeout)
        {
            return null;
        }

        await store.TouchAsync(sessionId, now, cancellationToken);

        return user;
    }

    /// <summary>Lists a person's active sessions, most recently active first.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The sessions.</returns>
    public Task<IReadOnlyList<AccessSession>> ListActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        store.ListActiveAsync(userId, ActiveSince(), cancellationToken);

    /// <summary>Ends the session of a person who signs out. Signing out is not an account change, so nothing is
    /// recorded.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="sessionId">Their session.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the session is ended.</returns>
    public Task SignOutAsync(Guid userId, string sessionId, CancellationToken cancellationToken) =>
        store.EndAsync(userId, sessionId, clock.GetUtcNow(), cancellationToken);

    /// <summary>Ends one session by hand, from the Account page or the Users screen.</summary>
    /// <param name="userId">Whose session it is.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="actorId">Who ends it.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><see langword="true"/> when an open session of that person was ended.</returns>
    public async Task<bool> EndAsync(Guid userId, string sessionId, Guid actorId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (!await store.EndAsync(userId, sessionId, now, cancellationToken))
        {
            return false;
        }

        await events.RecordAsync(userId, now, UserEventKind.SessionsEnded, actorId, "1", cancellationToken);

        return true;
    }

    /// <summary>Ends all of a person's sessions by hand, except the one making the request if given.</summary>
    /// <param name="userId">Whose sessions they are.</param>
    /// <param name="exceptId">The session to keep, or <see langword="null"/> to end every one.</param>
    /// <param name="actorId">Who ends them.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>How many sessions were ended.</returns>
    public async Task<int> EndAllAsync(
        Guid userId,
        string? exceptId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var ended = await store.EndAllAsync(userId, exceptId, now, cancellationToken);

        if (ended > 0)
        {
            await events.RecordAsync(
                userId,
                now,
                UserEventKind.SessionsEnded,
                actorId,
                ended.ToString(CultureInfo.InvariantCulture),
                cancellationToken);
        }

        return ended;
    }

    private DateTimeOffset ActiveSince() => clock.GetUtcNow() - options.Value.SessionIdleTimeout;
}
