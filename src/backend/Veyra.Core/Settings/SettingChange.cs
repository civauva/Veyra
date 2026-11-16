namespace Veyra.Core.Settings;

/// <summary>One setting about to change, as a module sees it when it says what the change will do.</summary>
/// <param name="Key">The configuration key.</param>
/// <param name="From">The value in effect now; null when nothing sets it.</param>
/// <param name="To">The value after the change; null when the change resets it to its default.</param>
public sealed record SettingChange(string Key, string? From, string? To);
