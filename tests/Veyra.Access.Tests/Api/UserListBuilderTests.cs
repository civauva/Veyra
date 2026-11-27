using Veyra.Access.Api;
using Veyra.Access.Users;
using Veyra.Core.Security;

namespace Veyra.Access.Tests.Api;

/// <summary>
/// The server filters and counts the Users screen (VEY-67): the browser only lays out what it receives.
/// </summary>
public class UserListBuilderTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private static readonly PersonSummary _ana =
        Person("ana@example.com", "Ana Admin", VeyraRole.Admin, factor: true, team: "Platform");

    private static readonly PersonSummary _tom =
        Person("tom@example.com", "Tom Editor", VeyraRole.Editor, team: "Payments");

    private static readonly PersonSummary _lena =
        Person("lena@example.com", "Lena Locked", VeyraRole.Viewer, lockedFor: TimeSpan.FromMinutes(5));

    private static readonly PersonSummary _old =
        Person("old@example.com", "Old Admin", VeyraRole.Admin, disabled: true);

    private static readonly PersonSummary _past =
        Person("past@example.com", "Past Lock", VeyraRole.Viewer, lockedFor: TimeSpan.FromMinutes(-5));

    private static readonly PersonSummary[] _everyone = [_ana, _tom, _lena, _old, _past];

    [Theory]
    [InlineData(PeopleShow.Everyone, new[] { "ana@", "tom@", "lena@", "old@", "past@" })]
    [InlineData(PeopleShow.NoFactor, new[] { "tom@", "lena@", "old@", "past@" })]
    [InlineData(PeopleShow.Locked, new[] { "lena@" })]
    [InlineData(PeopleShow.Off, new[] { "old@" })]
    public void Build_Show_ListsOnlyThatChip(PeopleShow show, string[] expected)
    {
        var page = UserListBuilder.Build(_everyone, null, show, _now);

        Assert.Equal(expected.Select(local => local + "example.com"), page.People.Select(person => person.Email));
    }

    [Theory]
    [InlineData("PAYMENTS", "tom@example.com")]
    [InlineData("lena@", "lena@example.com")]
    [InlineData("ana admin", "ana@example.com")]
    public void Build_Search_MatchesNameEmailOrTeamInAnyCase(string search, string expected)
    {
        var page = UserListBuilder.Build(_everyone, search, PeopleShow.Everyone, _now);

        Assert.Equal(expected, Assert.Single(page.People).Email);
    }

    [Fact]
    public void Build_Search_CountsEachChipWithinTheSearch()
    {
        var page = UserListBuilder.Build(_everyone, "admin", PeopleShow.Off, _now);

        Assert.Equal("old@example.com", Assert.Single(page.People).Email);
        Assert.Equal(new UserCountsResponse(2, 1, 0, 1), page.Counts);
    }

    [Fact]
    public void Build_Posture_CountsEveryoneWhateverTheFilter()
    {
        var page = UserListBuilder.Build(_everyone, "nobody", PeopleShow.Locked, _now);

        Assert.Empty(page.People);
        Assert.Equal(new UserPostureResponse(5, 1, 1, 1, 1, 3, 0), page.Posture);
        Assert.Equal(new UserRolesResponse(2, 1, 2), page.Roles);
    }

    [Fact]
    public void Build_Posture_CountsAnActiveAdminWithoutAFactorAsMissing()
    {
        var bare = Person("bare@example.com", "Bare Admin", VeyraRole.Admin);

        var page = UserListBuilder.Build([_ana, bare], null, PeopleShow.Everyone, _now);

        var posture = page.Posture;
        Assert.Equal(
            (2, 1, 1, 1),
            (posture.Admins, posture.AdminsWithSecondFactor, posture.Missing, posture.MissingAdmins));
    }

    private static PersonSummary Person(
        string email,
        string name,
        VeyraRole role,
        bool factor = false,
        bool disabled = false,
        TimeSpan? lockedFor = null,
        string? team = null) =>
        new(
            new AccessUser(
                Guid.NewGuid(),
                email,
                name,
                role,
                disabled,
                null,
                0,
                lockedFor is { } span ? _now + span : null,
                _now.AddDays(-90),
                factor ? _now.AddDays(-2) : _now.AddDays(-40),
                team),
            factor ? _now.AddDays(-60) : null,
            0,
            null);
}
