using Veyra.Core.Settings;

namespace Veyra.Access.Settings;

/// <summary>Tells other modules which settings an Admin saved, from <c>access.setting</c> (ADR 0017, 0028).</summary>
/// <param name="store">Where the settings are kept.</param>
internal sealed class SavedSettings(SettingStore store) : ISavedSettings
{
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSavedKeysAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        return store.SavedKeysAsync(prefix, cancellationToken);
    }
}
