namespace Veyra.Access.Users;

/// <summary>One person on the Users screen: the account with what the badges show (ADR 0011).</summary>
/// <param name="User">The account.</param>
/// <param name="SecondFactorSince">When their second factor was switched on, or <see langword="null"/>.</param>
/// <param name="ActiveSessions">How many browsers are signed in now.</param>
/// <param name="LastActiveAt">The newest activity of any of their sessions, or <see langword="null"/>.</param>
internal sealed record PersonSummary(
    AccessUser User,
    DateTimeOffset? SecondFactorSince,
    int ActiveSessions,
    DateTimeOffset? LastActiveAt);
