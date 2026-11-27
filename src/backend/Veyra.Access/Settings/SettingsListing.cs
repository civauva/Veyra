using Veyra.Access.Api;
using Veyra.Core.Settings;

namespace Veyra.Access.Settings;

/// <summary>
/// Decides what the Settings screen lists (VEY-67): the settings of a search or a section in the chosen view, and the
/// sections of the list beside them. The browser shows what this keeps and hides nothing itself.
/// </summary>
internal static class SettingsListing
{
    // Sections that hold no settings but pages of their own: listed in either view.
    private static readonly string[] _pages = ["Databases", "Agents"];

    // What the running installation says about itself: listed only in the Advanced view.
    private const string _system = "System";

    /// <summary>Builds the listing.</summary>
    /// <param name="definitions">Every setting, in screen order.</param>
    /// <param name="search">
    /// Words that a setting's label, card, help, or key must all hold, in any case. A search looks through every
    /// section in either view; null or blank lists the section.
    /// </param>
    /// <param name="section">The section, such as <c>Data</c>; an unknown one lists no settings.</param>
    /// <param name="advanced">Whether the Advanced view is chosen.</param>
    /// <returns>The keys to show, in screen order, and the sections to list.</returns>
    public static SettingsListingResponse Build(
        IEnumerable<SettingDefinition> definitions,
        string? search,
        string? section,
        bool advanced)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var all = definitions.OrderBy(definition => definition.Section).ToList();
        var words = (search ?? string.Empty).Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new SettingsListingResponse(
            [.. Shown(all, words, section, advanced).Select(definition => definition.Key)],
            [.. Sections(all, advanced)]);
    }

    private static IEnumerable<SettingDefinition> Shown(
        List<SettingDefinition> all,
        string[] words,
        string? section,
        bool advanced)
    {
        if (words.Length > 0)
        {
            return all.Where(definition => Matches(definition, words));
        }

        if (!Enum.TryParse<SettingSection>(section, ignoreCase: true, out var chosen) || !Enum.IsDefined(chosen))
        {
            return [];
        }

        // A section shows its basics in the Simple view, unless it has none, as when a link opens an advanced one.
        var inSection = all.Where(definition => definition.Section == chosen).ToList();
        var basics = inSection.Where(definition => !definition.Advanced).ToList();

        return advanced || basics.Count == 0 ? inSection : basics;
    }

    private static IEnumerable<string> Sections(List<SettingDefinition> all, bool advanced)
    {
        var withSettings = all
            .Where(definition => advanced || !definition.Advanced)
            .Select(definition => definition.Section)
            .Distinct()
            .Select(section => section.ToString());

        return advanced ? [.. withSettings, .. _pages, _system] : [.. withSettings, .. _pages];
    }

    private static bool Matches(SettingDefinition definition, string[] words)
    {
        var haystack = $"{definition.Label} {definition.Group} {definition.Help} {definition.Key}";

        return words.All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
