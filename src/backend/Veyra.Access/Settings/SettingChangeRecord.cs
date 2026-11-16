namespace Veyra.Access.Settings;

/// <summary>One change in the settings history.</summary>
/// <param name="Id">The change's number, which is also the settings version it produced.</param>
/// <param name="Name">The configuration key.</param>
/// <param name="OldValue">The value before; null when none was saved, and always null for a secret.</param>
/// <param name="NewValue">The value after; null when reset, and always null for a secret.</param>
/// <param name="Secret">Whether the setting is a secret, whose values are never recorded.</param>
/// <param name="Kind">How it came to change.</param>
/// <param name="ChangedBy">The account that saved it; null when it was removed since.</param>
/// <param name="ChangedByName">The name of who saved it.</param>
/// <param name="ChangedAt">When it was saved.</param>
/// <param name="Origin">The file name of an import; null otherwise.</param>
internal sealed record SettingChangeRecord(
    long Id,
    string Name,
    string? OldValue,
    string? NewValue,
    bool Secret,
    SettingChangeKind Kind,
    Guid? ChangedBy,
    string ChangedByName,
    DateTimeOffset ChangedAt,
    string? Origin);
