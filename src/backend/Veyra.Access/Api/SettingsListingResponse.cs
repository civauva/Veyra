namespace Veyra.Access.Api;

/// <summary>What the Settings screen lists, as the server decides it (VEY-67).</summary>
/// <param name="Keys">The keys of the settings to show, in screen order.</param>
/// <param name="Sections">
/// The sections of the list, by name: those with settings in the view, the Database monitoring and Agents pages, and
/// System in the Advanced view.
/// </param>
public sealed record SettingsListingResponse(IReadOnlyList<string> Keys, IReadOnlyList<string> Sections);
