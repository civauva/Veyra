using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veyra.Alerting.Notifications;

/// <summary>
/// Runs the <see cref="NotificationDispatcher"/> every <see cref="NotificationOptions.DispatchInterval"/> while Veyra
/// runs. A full batch is followed by the next one straight away, so a backlog drains without waiting.
/// </summary>
/// <param name="services">
/// Resolves the dispatcher on every run. It needs the database, which may not be configured; resolving it inside the
/// guarded run turns that into a logged failure instead of a crash at startup.
/// </param>
/// <param name="options">The Alerting settings with the interval and the batch size.</param>
/// <param name="clock">The clock behind the timer.</param>
/// <param name="logger">Receives failures of the run itself, which are retried on the next tick.</param>
internal sealed partial class NotificationDispatch(
    IServiceProvider services,
    IOptions<AlertingOptions> options,
    TimeProvider clock,
    ILogger<NotificationDispatch> logger) : BackgroundService
{
    /// <summary>Dispatches now and then on every tick until Veyra stops.</summary>
    /// <param name="stoppingToken">Signals that Veyra is stopping.</param>
    /// <returns>A task that completes when Veyra stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Notifications;

        using var timer = new PeriodicTimer(settings.DispatchInterval, clock);

        var failing = false;

        do
        {
            try
            {
                var dispatcher = services.GetRequiredService<NotificationDispatcher>();
                int tried;

                do
                {
                    tried = await dispatcher.DispatchAsync(stoppingToken);
                }
                while (tried >= settings.BatchSize);

                if (failing)
                {
                    LogRecovered();
                    failing = false;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Only the first failure in a row is a warning, so a database outage does not fill the log.
                if (failing)
                {
                    LogStillFailing(exception);
                }
                else
                {
                    LogFailed(exception);
                    failing = true;
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending notifications failed; retrying on the next run")]
    private partial void LogFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sending notifications is still failing")]
    private partial void LogStillFailing(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sending notifications works again")]
    private partial void LogRecovered();
}
