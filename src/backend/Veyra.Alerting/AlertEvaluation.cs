using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veyra.Alerting;

/// <summary>
/// Evaluates the alert rules at startup and then every <see cref="AlertingOptions.EvaluationInterval"/> while Veyra
/// runs.
/// </summary>
/// <param name="services">
/// Resolves the rules on every run. They need the database, which may not be configured; resolving them
/// inside the guarded run turns that into a logged failure instead of a crash at startup.
/// </param>
/// <param name="options">The Alerting settings with the interval.</param>
/// <param name="clock">The clock behind the timer.</param>
/// <param name="logger">Receives failures, which are retried on the next run.</param>
internal sealed partial class AlertEvaluation(
    IServiceProvider services,
    IOptions<AlertingOptions> options,
    TimeProvider clock,
    ILogger<AlertEvaluation> logger) : BackgroundService
{
    /// <summary>Evaluates now and then on every tick until Veyra stops.</summary>
    /// <param name="stoppingToken">Signals that Veyra is stopping.</param>
    /// <returns>A task that completes when Veyra stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.EvaluationInterval, clock);

        var failing = false;

        do
        {
            try
            {
                await services.GetRequiredService<AlertRuleRun>().RunAsync(stoppingToken);

                if (failing)
                {
                    LogRecovered();
                    failing = false;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A database outage must not stop evaluation for good. Only the first failure in a row is a warning,
                // so an outage does not fill the log every 30 seconds.
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alert evaluation failed; retrying on the next run")]
    private partial void LogFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Alert evaluation is still failing")]
    private partial void LogStillFailing(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Alert evaluation works again")]
    private partial void LogRecovered();
}
