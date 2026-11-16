namespace Veyra.Access.Settings;

/// <summary>One setting a save writes.</summary>
/// <param name="Name">The configuration key, such as <c>Veyra:Storage:LogRetentionDays</c>.</param>
/// <param name="Value">The new value; null to remove the saved value, so the default applies again.</param>
/// <param name="Secret">Whether the value is stored encrypted and kept out of the history.</param>
internal sealed record SettingWrite(string Name, string? Value, bool Secret);
