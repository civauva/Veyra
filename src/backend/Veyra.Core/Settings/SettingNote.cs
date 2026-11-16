namespace Veyra.Core.Settings;

/// <summary>What a change will do, shown on the review before it is saved (ADR 0017).</summary>
/// <param name="Key">The setting the note is about.</param>
/// <param name="Text">What will happen, such as "Deletes the 7 oldest days of logs, 2.3 GB."</param>
/// <param name="Risky">
/// Whether it deletes data or may lock people out, so saving needs an explicit confirmation.
/// </param>
public sealed record SettingNote(string Key, string Text, bool Risky);
