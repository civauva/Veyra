using Veyra.Access.About;

namespace Veyra.Access.Tests.About;

public sealed class ReleaseNotesTests
{
    private const string _changelog = """
        # Changelog

        Every release of Veyra, newest first.

        ## [Unreleased]

        ### Added

        - Something not released yet.

        ## [0.4.1] - 2026-11-27

        Drill-in tiles and release notes.

        ### Added

        - Tiles open their details.
        - Release notes behind the version.

        ### Fixed

        - Collector health shows an online agent in green.

        ## [0.2.1] - 2026-11-20

        ### Changed

        - The first tagged release.
        """;

    [Fact]
    public void Parse_Changelog_ReadsEachReleaseNewestFirstWithItsSections()
    {
        var releases = ReleaseNotes.Parse(_changelog);

        Assert.Equal(["0.4.1", "0.2.1"], releases.Select(release => release.Version));

        var latest = releases[0];
        Assert.Equal(new DateOnly(2026, 11, 27), latest.Date);
        Assert.Equal("Drill-in tiles and release notes.", latest.Summary);
        Assert.Equal(["Added", "Fixed"], latest.Sections.Select(section => section.Title));
        Assert.Equal(
            ["Tiles open their details.", "Release notes behind the version."],
            latest.Sections[0].Items);
        Assert.Null(releases[1].Summary);
    }

    [Fact]
    public void Parse_UnreleasedHeading_IsLeftOut()
    {
        var releases = ReleaseNotes.Parse(_changelog);

        Assert.DoesNotContain(releases, release => release.Version == "Unreleased");
    }

    [Fact]
    public void Parse_ItemOverSeveralLines_IsOneItem()
    {
        var releases = ReleaseNotes.Parse("""
            ## [1.0.0] - 2027-01-01

            ### Added

            - A long item that runs
              over two lines.
            - Another.
            """);

        Assert.Equal(["A long item that runs over two lines.", "Another."], releases[0].Sections[0].Items);
    }

    [Fact]
    public void Embedded_NewestRelease_IsTheVersionThisBuildCarries()
    {
        // Each version gets its notes before it ships: a build whose version has no entry fails here.
        Assert.Equal(VeyraVersion.Current, ReleaseNotes.Embedded[0].Version);
    }

    [Fact]
    public void Current_Version_HasNoBuildSuffix()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", VeyraVersion.Current);
    }
}
