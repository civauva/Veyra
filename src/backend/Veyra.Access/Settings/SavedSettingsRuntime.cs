using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Veyra.Access.Settings;

/// <summary>
/// The application's handle on the saved-settings configuration provider: switches it fully on once the database is
/// migrated and Data Protection exists, and refreshes it after a save (ADR 0017).
/// </summary>
/// <remarks>
/// Without storage there is no provider, and every method does nothing, so the rest of the module needs no checks.
/// </remarks>
/// <param name="configuration">The application's configuration.</param>
internal sealed class SavedSettingsRuntime(IConfiguration configuration)
{
    private readonly SavedSettingsConfigurationProvider? _provider = (configuration as IConfigurationRoot)?
        .Providers
        .OfType<SavedSettingsConfigurationProvider>()
        .LastOrDefault();

    /// <summary>Whether settings can be saved at all, which needs storage.</summary>
    public bool Enabled => _provider is not null;

    /// <summary>The number of the last change the configuration has read; 0 without storage.</summary>
    public long Version => _provider?.Version ?? 0;

    /// <summary>Reads secrets from now on, and loads again now that the schema is migrated.</summary>
    /// <param name="protection">The application's Data Protection.</param>
    public void Activate(IDataProtectionProvider protection) => _provider?.Activate(protection);

    /// <summary>Reads the saved settings again, so every <c>IOptionsMonitor</c> sees a save at once.</summary>
    public void Refresh() => _provider?.Refresh();
}
