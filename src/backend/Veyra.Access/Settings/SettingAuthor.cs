namespace Veyra.Access.Settings;

/// <summary>Who saved a change, for the history.</summary>
/// <param name="UserId">The account; null when the change was not made by a person.</param>
/// <param name="Name">The name shown in the history, kept after the account is removed.</param>
internal sealed record SettingAuthor(Guid? UserId, string Name);
