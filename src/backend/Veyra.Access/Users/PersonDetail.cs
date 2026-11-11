using Veyra.Access.History;
using Veyra.Access.SecondFactor;
using Veyra.Access.Sessions;

namespace Veyra.Access.Users;

/// <summary>Everything the person panel and the Account page show about one account (ADR 0011).</summary>
/// <param name="User">The account.</param>
/// <param name="AddedBy">The name of the Admin who added the account, or <see langword="null"/>.</param>
/// <param name="SecondFactor">Their second factor when it is switched on, or <see langword="null"/>.</param>
/// <param name="SecondFactorRequired">Whether the policy requires one of them.</param>
/// <param name="LastActiveAt">The newest activity of any of their sessions, or <see langword="null"/>.</param>
/// <param name="Sessions">Their active sessions, most recently active first.</param>
/// <param name="SignIns">Their latest sign-in attempts, newest first.</param>
/// <param name="SignedInLast30Days">Successful sign-ins within 30 days.</param>
/// <param name="FailedLast30Days">Failed sign-in attempts within 30 days.</param>
/// <param name="Events">Changes to the account, newest first.</param>
internal sealed record PersonDetail(
    AccessUser User,
    string? AddedBy,
    SecondFactorState? SecondFactor,
    bool SecondFactorRequired,
    DateTimeOffset? LastActiveAt,
    IReadOnlyList<AccessSession> Sessions,
    IReadOnlyList<SignInEvent> SignIns,
    int SignedInLast30Days,
    int FailedLast30Days,
    IReadOnlyList<UserEvent> Events);
