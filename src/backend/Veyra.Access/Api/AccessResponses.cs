using Veyra.Access.History;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;
using Veyra.Access.Users;

namespace Veyra.Access.Api;

/// <summary>Turns the module's records into the REST answers, the same way for every controller.</summary>
internal static class AccessResponses
{
    /// <summary>The signed-in user.</summary>
    /// <param name="user">The user.</param>
    /// <param name="options">The Access settings in effect now, for the default look and whether people choose.</param>
    /// <returns>The answer.</returns>
    public static SessionResponse Session(AccessUser user, AccessOptions options) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        user.TimeZone,
        new LookResponse(user.Look, options.DefaultLook, options.PeopleChooseLook));

    /// <summary>A person as the Users list shows them.</summary>
    /// <param name="person">The person.</param>
    /// <returns>The answer.</returns>
    public static UserResponse User(PersonSummary person) => new(
        person.User.Id,
        person.User.Email,
        person.User.DisplayName,
        person.User.Role,
        person.User.Disabled,
        person.User.CreatedAt,
        person.User.LastSignInAt,
        person.User.Team,
        person.User.LockedUntil,
        person.SecondFactorSince,
        person.ActiveSessions,
        person.LastActiveAt);

    /// <summary>A switched-on second factor.</summary>
    /// <param name="state">The second factor, or <see langword="null"/>.</param>
    /// <returns>The answer, or <see langword="null"/> when it is not on.</returns>
    public static SecondFactorInfoResponse? SecondFactor(SecondFactorState? state) =>
        state is { EnabledAt: { } since } ? new SecondFactorInfoResponse(since, state.RecoveryCodesLeft) : null;

    /// <summary>A session.</summary>
    /// <param name="session">The session.</param>
    /// <param name="currentId">The session making the request, or <see langword="null"/>.</param>
    /// <returns>The answer.</returns>
    public static SessionInfoResponse SessionInfo(AccessSession session, string? currentId) => new(
        session.Id,
        UserAgents.Describe(session.UserAgent),
        session.Address,
        session.StartedAt,
        session.LastActiveAt,
        session.Method,
        session.Id == currentId);

    /// <summary>A sign-in attempt.</summary>
    /// <param name="attempt">The attempt.</param>
    /// <returns>The answer.</returns>
    public static SignInAttemptResponse SignIn(SignInEvent attempt) => new(
        attempt.At,
        attempt.Outcome,
        attempt.Address,
        UserAgents.Describe(attempt.UserAgent),
        attempt.NewAddress);

    /// <summary>A change to an account.</summary>
    /// <param name="change">The change.</param>
    /// <returns>The answer.</returns>
    public static UserEventResponse Event(UserEvent change) => new(
        change.At,
        change.Kind,
        change.ActorName,
        change.Detail);

    /// <summary>A person in full, for the person panel.</summary>
    /// <param name="detail">The person.</param>
    /// <param name="days">Their sign-in attempts per day, the oldest first.</param>
    /// <returns>The answer.</returns>
    public static UserDetailResponse Detail(PersonDetail detail, IReadOnlyList<SignInDay> days) => new(
        User(new PersonSummary(
            detail.User,
            detail.SecondFactor?.EnabledAt,
            detail.Sessions.Count,
            detail.LastActiveAt)),
        detail.AddedBy,
        SecondFactor(detail.SecondFactor),
        detail.SecondFactorRequired,
        [.. detail.Sessions.Select(one => SessionInfo(one, null))],
        [.. detail.SignIns.Select(SignIn)],
        detail.SignedInLast30Days,
        detail.FailedLast30Days,
        [.. detail.Events.Select(Event)],
        new SignInDaysResponse(
            [.. days.Select(day => new SignInDayResponse(day.Day, day.SignedIn, day.Failed))],
            days.Sum(day => day.SignedIn),
            days.Sum(day => day.Failed)));
}
