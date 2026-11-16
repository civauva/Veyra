namespace Veyra.Access.Settings;

/// <summary>A page of the settings history, newest first.</summary>
/// <param name="Changes">The changes on this page.</param>
/// <param name="Next">The change number to ask for the next page with; null when this is the last page.</param>
internal sealed record SettingHistoryPage(IReadOnlyList<SettingChangeRecord> Changes, long? Next);
