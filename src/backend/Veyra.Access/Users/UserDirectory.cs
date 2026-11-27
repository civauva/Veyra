using Microsoft.Extensions.Options;
using Veyra.Access.History;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;
using Veyra.Access.Settings;
using Veyra.Core.Security;
using Veyra.Core.Time;

namespace Veyra.Access.Users;

/// <summary>
/// Reads people for the Users screen and the Account page: accounts together with their second factor, sessions,
/// sign-ins, and history (ADR 0011).
/// </summary>
/// <param name="users">Where users are stored.</param>
/// <param name="sessions">Where sessions are stored.</param>
/// <param name="signIns">Where sign-in attempts are stored.</param>
/// <param name="events">Where account changes are stored.</param>
/// <param name="secondFactors">Where second factors are stored.</param>
/// <param name="settings">Where the second-factor policy is stored.</param>
/// <param name="options">The Access settings, for the idle timeout.</param>
/// <param name="clock">The clock for what counts as active and recent.</param>
internal sealed class UserDirectory(
    UserStore users,
    SessionStore sessions,
    SignInEventStore signIns,
    UserEventStore events,
    SecondFactorStore secondFactors,
    AccessSettingStore settings,
    IOptionsMonitor<AccessOptions> options,
    TimeProvider clock)
{
    /// <summary>How many sign-in attempts the detail lists.</summary>
    public const int SignInsShown = 50;

    /// <summary>How many days the person panel's chart of sign-ins covers.</summary>
    public const int SignInDaysShown = 14;

    /// <summary>Lists everyone with what their badge shows, ordered by email.</summary>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The people.</returns>
    public async Task<IReadOnlyList<PersonSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await users.ListAsync(cancellationToken);
        var enabled = await secondFactors.ListEnabledAsync(cancellationToken);
        var active = await sessions.SummarizeAsync(ActiveSince(), cancellationToken);

        return
        [
            .. all.Select(user => new PersonSummary(
                user,
                enabled.TryGetValue(user.Id, out var since) ? since : null,
                active.TryGetValue(user.Id, out var summary) ? summary.Active : 0,
                active.TryGetValue(user.Id, out var last) ? last.LastActive : null)),
        ];
    }

    /// <summary>Reads one person in full.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The person, or <see langword="null"/> when no account has that ID.</returns>
    public async Task<PersonDetail?> DetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(userId, cancellationToken) is not { } user)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var secondFactor = await secondFactors.FindAsync(userId, cancellationToken);
        var open = await sessions.ListActiveAsync(userId, ActiveSince(), cancellationToken);
        var latest = await signIns.ListAsync(userId, SignInsShown, cancellationToken);
        var (signedIn, failed) = await signIns.CountAsync(userId, now.AddDays(-30), cancellationToken);
        var history = await events.ListAsync(userId, cancellationToken);
        var lastActive = (await sessions.SummarizeAsync(ActiveSince(), cancellationToken))
            .TryGetValue(userId, out var summary)
                ? summary.LastActive
                : (DateTimeOffset?)null;
        var required = (await settings.GetSecondFactorPolicyAsync(cancellationToken)).Requires(user);

        return new PersonDetail(
            user,
            history.LastOrDefault(one => one.Kind == UserEventKind.Added)?.ActorName,
            secondFactor is { Enabled: true } ? secondFactor : null,
            required,
            lastActive,
            open,
            latest,
            signedIn,
            failed,
            history);
    }

    /// <summary>
    /// Counts a person's sign-in attempts per day over the last two weeks, on the calendar of a time zone, so the
    /// chart counts every attempt and not only those the detail lists (VEY-68).
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="zone">The time zone whose midnight starts each day.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Fourteen days, the oldest first, today last.</returns>
    public async Task<IReadOnlyList<SignInDay>> SignInDaysAsync(
        Guid userId,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var now = clock.GetUtcNow();

        // Fifteen midnights bound fourteen days; the last is tomorrow's.
        var starts = Enumerable.Range(-1, SignInDaysShown + 1)
            .Reverse()
            .Select(daysBack => TimeZones.LocalMidnight(now, zone, daysBack))
            .ToList();
        var counts = await signIns.CountByPeriodAsync(userId, starts, cancellationToken);

        return
        [
            .. counts.Select((count, index) => new SignInDay(
                TimeZones.LocalDay(starts[index], zone),
                count.SignedIn,
                count.Failed)),
        ];
    }

    private DateTimeOffset ActiveSince() => clock.GetUtcNow() - options.CurrentValue.SessionIdleTimeout;
}
