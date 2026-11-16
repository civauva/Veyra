namespace Veyra.Access.Settings;

/// <summary>Which changes of the settings history to read.</summary>
/// <param name="Prefix">Only settings whose key starts with this, such as <c>Veyra:Storage:</c>; null for all.</param>
/// <param name="ChangedBy">Only changes saved by this account; null for everyone.</param>
/// <param name="From">Only changes saved at or after this time; null for no limit.</param>
/// <param name="Before">Only changes older than this change number, for the next page; null for the newest.</param>
/// <param name="Limit">The most changes on one page.</param>
/// <param name="Names">Only these settings, such as the keys of one section; null for all.</param>
internal sealed record SettingHistoryQuery(
    string? Prefix = null,
    Guid? ChangedBy = null,
    DateTimeOffset? From = null,
    long? Before = null,
    int Limit = 50,
    IReadOnlyList<string>? Names = null);
