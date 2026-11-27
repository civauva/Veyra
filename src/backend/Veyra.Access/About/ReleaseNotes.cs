using System.Globalization;
using Veyra.Access.Api;

namespace Veyra.Access.About;

/// <summary>
/// The release notes, read from <c>CHANGELOG.md</c> as Keep a Changelog writes it (VEY-82): a <c>## [version] - date</c>
/// heading for each release, newest first, an optional sentence under it, and <c>### Added</c>, <c>### Changed</c>,
/// <c>### Fixed</c>, or <c>### Removed</c> with one bullet a change. The Unreleased heading is left out: those changes
/// do not run anywhere yet.
/// </summary>
internal static class ReleaseNotes
{
    private const string _resource = "Veyra.Access.About.CHANGELOG.md";

    private static readonly Lazy<IReadOnlyList<ReleaseResponse>> _embedded = new(ReadEmbedded);

    /// <summary>The notes this build carries, newest first.</summary>
    public static IReadOnlyList<ReleaseResponse> Embedded => _embedded.Value;

    /// <summary>Reads the releases of a changelog.</summary>
    /// <param name="markdown">The changelog.</param>
    /// <returns>Each release, in the order the file lists them.</returns>
    public static IReadOnlyList<ReleaseResponse> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var releases = new List<Draft>();
        Draft? release = null;
        DraftSection? section = null;
        var inItem = false;

        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                release = Heading(line[3..]);
                section = null;
                inItem = false;

                if (release is not null)
                {
                    releases.Add(release);
                }

                continue;
            }

            if (release is null || line.Length == 0)
            {
                inItem = false;
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                section = new DraftSection(line[4..].Trim());
                release.Sections.Add(section);
                inItem = false;
            }
            else if (section is not null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                section.Items.Add(line[2..].Trim());
                inItem = true;
            }
            else if (section is not null && inItem && char.IsWhiteSpace(raw[0]))
            {
                // An item that runs over several lines is one item.
                section.Items[^1] = $"{section.Items[^1]} {line.Trim()}";
            }
            else if (section is null)
            {
                release.Summary = release.Summary is null ? line.Trim() : $"{release.Summary} {line.Trim()}";
            }
        }

        return releases
            .Select(draft => new ReleaseResponse(
                draft.Version,
                draft.Date,
                draft.Summary,
                draft.Sections.Select(one => new ReleaseSectionResponse(one.Title, one.Items)).ToList()))
            .ToList();
    }

    /// <summary>A release from its heading, or null for Unreleased and anything that is not a release.</summary>
    private static Draft? Heading(string heading)
    {
        var open = heading.IndexOf('[', StringComparison.Ordinal);
        var close = heading.IndexOf(']', StringComparison.Ordinal);

        if (open < 0 || close < open)
        {
            return null;
        }

        var version = heading[(open + 1)..close].Trim();

        if (version.Length == 0 || !char.IsDigit(version[0]))
        {
            return null;
        }

        var rest = heading[(close + 1)..].Trim().TrimStart('-').Trim();
        DateOnly? date = DateOnly.TryParseExact(rest, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

        return new Draft(version, date);
    }

    private static IReadOnlyList<ReleaseResponse> ReadEmbedded()
    {
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(_resource)
            ?? throw new InvalidOperationException($"The release notes {_resource} are not embedded in Veyra.Access.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    /// <summary>A release while its lines are read.</summary>
    private sealed class Draft(string version, DateOnly? date)
    {
        public string Version { get; } = version;

        public DateOnly? Date { get; } = date;

        public string? Summary { get; set; }

        public List<DraftSection> Sections { get; } = [];
    }

    /// <summary>A section while its lines are read.</summary>
    private sealed class DraftSection(string title)
    {
        public string Title { get; } = title;

        public List<string> Items { get; } = [];
    }
}
