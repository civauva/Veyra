using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>
/// Puts the Users screen together on the server (VEY-67): it applies the search and the chip, and counts what the
/// chips, the rings, and the role bar show, so the browser filters and counts nothing.
/// </summary>
internal static class UserListBuilder
{
    // How recent a sign-in must be for the Recent sign-ins ring.
    private static readonly TimeSpan _recent = TimeSpan.FromDays(30);

    /// <summary>Builds the screen's answer.</summary>
    /// <param name="everyone">Everyone, ordered by email.</param>
    /// <param name="search">Text a name, email, or team must hold, in any case; null or blank for everyone.</param>
    /// <param name="show">The chip.</param>
    /// <param name="now">The time a lock is compared with.</param>
    /// <returns>The answer.</returns>
    public static UserListResponse Build(
        IReadOnlyList<PersonSummary> everyone,
        string? search,
        PeopleShow show,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(everyone);

        var needle = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var matching = everyone.Where(person => Matches(person, needle)).ToList();

        return new UserListResponse(
            [.. matching.Where(person => Shows(person, show, now)).Select(AccessResponses.User)],
            new UserCountsResponse(
                matching.Count,
                matching.Count(person => Shows(person, PeopleShow.NoFactor, now)),
                matching.Count(person => Shows(person, PeopleShow.Locked, now)),
                matching.Count(person => Shows(person, PeopleShow.Off, now))),
            Posture(everyone, now),
            new UserRolesResponse(
                everyone.Count(person => person.User.Role == VeyraRole.Admin),
                everyone.Count(person => person.User.Role == VeyraRole.Editor),
                everyone.Count(person => person.User.Role == VeyraRole.Viewer)));
    }

    private static UserPostureResponse Posture(IReadOnlyList<PersonSummary> everyone, DateTimeOffset now)
    {
        var admins = everyone.Where(person => person.User.Role == VeyraRole.Admin && !person.User.Disabled).ToList();
        var missing = everyone.Where(person => !person.User.Disabled && person.SecondFactorSince is null).ToList();

        return new UserPostureResponse(
            everyone.Count,
            everyone.Count(person => person.SecondFactorSince is not null),
            everyone.Count(person => person.User.LastSignInAt is { } last && now - last <= _recent),
            admins.Count,
            admins.Count(person => person.SecondFactorSince is not null),
            missing.Count,
            missing.Count(person => person.User.Role == VeyraRole.Admin));
    }

    private static bool Matches(PersonSummary person, string? needle) =>
        needle is null
        || person.User.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || person.User.Email.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || (person.User.Team?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool Shows(PersonSummary person, PeopleShow show, DateTimeOffset now) => show switch
    {
        PeopleShow.NoFactor => person.SecondFactorSince is null,
        PeopleShow.Locked => person.User.LockedUntil is { } until && until > now,
        PeopleShow.Off => person.User.Disabled,
        _ => true,
    };
}
