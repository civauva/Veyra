namespace Veyra.Access.Settings;

/// <summary>A value saved on the Settings screen.</summary>
/// <param name="Value">The value, decrypted when it is a secret.</param>
/// <param name="Secret">Whether it is stored encrypted.</param>
/// <param name="ChangedByName">Who saved it last; null for values saved before the history existed.</param>
/// <param name="ChangedAt">When it was saved last; null for values saved before the history existed.</param>
internal sealed record SavedSetting(string Value, bool Secret, string? ChangedByName, DateTimeOffset? ChangedAt);
