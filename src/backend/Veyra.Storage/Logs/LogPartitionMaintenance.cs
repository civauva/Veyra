using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veyra.Storage.Logs;

/// <summary>
/// Keeps log storage in shape while Veyra runs: partitions ahead of the clock, and old or excess logs removed.
/// Runs once at startup and then every hour.
/// </summary>
/// <param name="partitions">Creates the daily partitions.</param>
/// <param name="retention">Removes logs that are too old or over the size budget.</param>
/// <param name="clock">The clock behind the hourly timer.</param>
/// <param name="logger">Receives failures, which are retried on the next run.</param>
internal sealed partial class LogPartitionMaintenance(
    LogPartitions partitions,
    LogRetention retention,
    TimeProvider clock,
    ILogger<LogPartitionMaintenance> logger) : BackgroundService
{
    // Hourly is far more often than daily partitions need, and keeps the size budget reasonably tight.
    private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

    /// <summary>Runs maintenance now and then every hour until Veyra stops.</summary>
    /// <param name="stoppingToken">Signals that Veyra is stopping.</param>
    /// <returns>A task that completes when Veyra stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, clock);

        do
        {
            try
            {
                await partitions.EnsureAsync(stoppingToken);
                await retention.ApplyAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A short database outage must not stop maintenance for good; the next run tries again.
                LogFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Log storage maintenance failed; retrying in an hour")]
    private partial void LogFailed(Exception exception);
}
