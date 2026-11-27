namespace Veyra.Access.Api;

/// <summary>
/// The Users screen (ADR 0011, VEY-67): the people a search and a chip keep, with the figures the screen shows, all
/// worked out by the server.
/// </summary>
/// <param name="People">The people that match, ordered by email.</param>
/// <param name="Counts">How many of the people that match the search each chip would list.</param>
/// <param name="Posture">How well everyone's accounts are protected, whatever the search.</param>
/// <param name="Roles">How many people hold each role, whatever the search.</param>
public sealed record UserListResponse(
    IReadOnlyList<UserResponse> People,
    UserCountsResponse Counts,
    UserPostureResponse Posture,
    UserRolesResponse Roles);
