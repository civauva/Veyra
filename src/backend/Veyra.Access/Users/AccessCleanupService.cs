using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veyra.Access.Users;

/// <summary>Runs <see cref="AccessCleanup"/> at startup and then every hour.</summary>
/// <remarks>
/// The clean-up is resolved on each run, like the alert evaluation, so Veyra starts without a configured database and
/// only the run fails, with a log entry that says what to set.
/// </remarks>
/// <param name="services">The application services, for the clean-up.</param>
/// <param name="clock">The clock for the hourly timer.</param>
/// <param name="logger">Receives what was deleted and failures.</param>
internal sealed partial class AccessCleanupService(
    IServiceProvider services,
    TimeProvider clock,
    ILogger<AccessCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

    /// <summary>Cleans up until Veyra stops. A failed run is logged and tried again an hour later.</summary>
    /// <param name="stoppingToken">Signals that Veyra is stopping.</param>
    /// <returns>A task that completes when Veyra stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, clock);

        do
        {
            try
            {
                var deleted = await services.GetRequiredService<AccessCleanup>().RunAsync(stoppingToken);

                if (deleted > 0)
                {
                    LogDeleted(deleted);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} old sign-in attempts and sessions")]
    private partial void LogDeleted(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Cleaning up old sign-in attempts and sessions failed")]
    private partial void LogFailed(Exception exception);
}
