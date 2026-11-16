using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Veyra.Access.Settings;

/// <summary>
/// Looks every 15 seconds whether settings were saved elsewhere, such as by a second Veyra instance, and reloads them
/// when they were. A save on this instance reloads at once and needs no wait (ADR 0017).
/// </summary>
/// <param name="runtime">The saved-settings configuration.</param>
/// <param name="services">Gives the store, which exists only when storage is configured.</param>
/// <param name="clock">Drives the timer.</param>
/// <param name="logger">Logs a failed look, at debug level, so an outage does not fill the log.</param>
internal sealed partial class SavedSettingsRefreshService(
    SavedSettingsRuntime runtime,
    IServiceProvider services,
    TimeProvider clock,
    ILogger<SavedSettingsRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Without storage nothing can be saved, so there is nothing to look for.
        if (!runtime.Enabled)
        {
            return;
        }

        var store = services.GetRequiredService<SettingStore>();
        using var timer = new PeriodicTimer(_interval, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (await store.VersionAsync(stoppingToken) != runtime.Version)
                {
                    runtime.Refresh();
                }
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
            {
                LogRefreshFailed(exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not look for settings saved elsewhere")]
    private partial void LogRefreshFailed(Exception exception);
}
