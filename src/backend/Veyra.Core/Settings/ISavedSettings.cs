namespace Veyra.Core.Settings;

/// <summary>
/// Tells which settings an Admin saved on the Settings screen (ADR 0017), so a module can say what is still at its
/// default, as the integration page's next steps do (ADR 0028). The Access module, which keeps the settings,
/// implements it.
/// </summary>
public interface ISavedSettings
{
    /// <summary>Lists the keys of the saved settings that start with a prefix.</summary>
    /// <param name="prefix">
    /// The start of the keys, such as <c>Veyra:Alerting:Database</c>; matched ignoring case.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The keys, in no set order. A setting that was reset to its default is not saved.</returns>
    Task<IReadOnlyList<string>> ListSavedKeysAsync(string prefix, CancellationToken cancellationToken);
}
