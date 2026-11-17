using Veyra.Core.Settings;

namespace Veyra.Access.Settings;

/// <summary>One change in the settings history: to a setting, or to a monitored system or agent.</summary>
/// <param name="Id">The change's number; for a setting, also the settings version it produced.</param>
/// <param name="Name">The configuration key, or the name of the monitored system or agent.</param>
/// <param name="OldValue">The value before; null when none was saved, and always null for a secret.</param>
/// <param name="NewValue">The value after; null when reset, and always null for a secret.</param>
/// <param name="Secret">Whether the setting is a secret, whose values are never recorded.</param>
/// <param name="Kind">How it came to change.</param>
/// <param name="ChangedBy">The account that saved it; null when it was removed since.</param>
/// <param name="ChangedByName">The name of who saved it.</param>
/// <param name="ChangedAt">When it was saved.</param>
/// <param name="Origin">The file name of an import; null otherwise.</param>
/// <param name="Subject">The kind of monitored system or agent it is about; null for a setting.</param>
/// <param name="Fields">For a change to a monitored system or agent, the fields it touched; null otherwise.</param>
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
    string? Origin,
    MonitoringSubject? Subject = null,
    IReadOnlyList<string>? Fields = null);
