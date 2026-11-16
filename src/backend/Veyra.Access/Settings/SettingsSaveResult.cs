using Veyra.Access.Api;

namespace Veyra.Access.Settings;

/// <summary>How a save of settings ended.</summary>
/// <param name="Page">The settings after the save; null when nothing was saved.</param>
/// <param name="Review">Why nothing was saved, when the review refused it; null otherwise.</param>
/// <param name="IsConflict">Whether another save came first, so nothing was saved.</param>
internal sealed record SettingsSaveResult(SettingsPageResponse? Page, SettingsReviewResponse? Review, bool IsConflict)
{
    /// <summary>The changes were saved.</summary>
    /// <param name="page">The settings after the save.</param>
    /// <returns>The result.</returns>
    public static SettingsSaveResult Saved(SettingsPageResponse page) => new(page, null, false);

    /// <summary>The review refused the changes: a problem, or a risky change without confirmation.</summary>
    /// <param name="review">The review.</param>
    /// <returns>The result.</returns>
    public static SettingsSaveResult Refused(SettingsReviewResponse review) => new(null, review, false);

    /// <summary>Another save came first.</summary>
    /// <returns>The result.</returns>
    public static SettingsSaveResult Conflict() => new(null, null, true);
}
