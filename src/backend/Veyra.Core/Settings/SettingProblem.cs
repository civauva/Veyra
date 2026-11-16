namespace Veyra.Core.Settings;

/// <summary>Why a candidate configuration cannot be saved.</summary>
/// <param name="Key">
/// The setting the problem is about, such as <c>Veyra:Storage:LogRetentionDays</c>, so the screen can show it next to
/// the field; null when it concerns no single setting.
/// </param>
/// <param name="Message">What is wrong, in words a person can act on.</param>
public sealed record SettingProblem(string? Key, string Message);
