using Veyra.Core.Security;

namespace Veyra.Access.Users;

/// <summary>
/// A person who can sign in to Veyra, as stored in <c>access.user_account</c>.
/// </summary>
/// <param name="Id">The user's ID.</param>
/// <param name="Email">The email address as the person typed it.</param>
/// <param name="DisplayName">The name shown in the UI.</param>
/// <param name="Role">What the user may do.</param>
/// <param name="Disabled">Whether the user is blocked from signing in.</param>
/// <param name="PasswordHash">The PBKDF2 hash, or <see langword="null"/> for single sign-on only.</param>
/// <param name="FailedSignIns">Failed sign-ins in a row since the last success or lockout.</param>
/// <param name="LockedUntil">When a lockout ends, or <see langword="null"/> when not locked.</param>
/// <param name="CreatedAt">When the user was created.</param>
/// <param name="LastSignInAt">When the user last signed in, or <see langword="null"/> if never.</param>
/// <param name="Team">A free-text team an Admin set, or <see langword="null"/> (ADR 0011).</param>
internal sealed record AccessUser(
    Guid Id,
    string Email,
    string DisplayName,
    VeyraRole Role,
    bool Disabled,
    string? PasswordHash,
    int FailedSignIns,
    DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt,
    string? Team = null)
{
    /// <summary>The longest team name an Admin can set.</summary>
    public const int MaxTeamLength = 100;

    /// <summary>
    /// Returns the form of an email address used for lookups and uniqueness: trimmed and lower case.
    /// </summary>
    /// <param name="email">The email address as typed.</param>
    /// <returns>The normalised address.</returns>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
