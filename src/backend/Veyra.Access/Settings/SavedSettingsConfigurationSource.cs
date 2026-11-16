using Microsoft.Extensions.Configuration;

namespace Veyra.Access.Settings;

/// <summary>
/// Puts the settings saved on the Settings screen into the application's configuration, under the same keys the
/// modules bind, so every module follows them without knowing where they come from (ADR 0017).
/// </summary>
/// <remarks>
/// The host adds it last, so a saved value wins over <c>appsettings.json</c> and the environment.
/// </remarks>
/// <param name="connectionString">The PostgreSQL connection string, from the environment.</param>
public sealed class SavedSettingsConfigurationSource(string connectionString) : IConfigurationSource
{
    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new SavedSettingsConfigurationProvider(connectionString);
}
