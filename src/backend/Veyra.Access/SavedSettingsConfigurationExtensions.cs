using Microsoft.Extensions.Configuration;
using Veyra.Access.Settings;
using Veyra.Core;

namespace Veyra.Access;

/// <summary>Adds the settings saved on the Settings screen to the application's configuration (ADR 0017).</summary>
public static class SavedSettingsConfigurationExtensions
{
    /// <summary>
    /// Adds the saved settings as the last configuration source, so a value saved on the Settings screen wins over
    /// <c>appsettings.json</c> and the environment, which give the first values.
    /// </summary>
    /// <remarks>
    /// Call it after every other source, before the modules are registered. Without a connection string there is
    /// nothing saved, and nothing is added.
    /// </remarks>
    /// <param name="configuration">The application's configuration.</param>
    /// <returns>The same <paramref name="configuration"/>, so calls can be chained.</returns>
    public static IConfigurationManager AddVeyraSavedSettings(this IConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration[$"{VeyraOptionNames.Storage}:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            configuration.Add(new SavedSettingsConfigurationSource(connectionString));
        }

        return configuration;
    }
}
